# Локальный деплой тестовой ленты без ASDC-сервера.
# Копирует Release DLL + NC_TestRibbon.json в %LOCALAPPDATA%\ASDC\LoadFile\NANOCAD
# и (опционально) регистрирует DLL в Cfg.ini Startup всех найденных профилей nanoCAD.
#
# Запускать при ЗАКРЫТОМ nanoCAD:
#   .\dev-deploy.ps1
#   .\dev-deploy.ps1 -RegisterStartup
#   .\dev-deploy.ps1 -Configuration Debug

[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [switch]$RegisterStartup
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projDir = Join-Path $root 'NcTestRibbon'
$dll = Join-Path $projDir "bin\$Configuration\net6.0-windows\NC_TestRibbon.dll"
$json = Join-Path $root 'NC_TestRibbon.json'
$dest = Join-Path $env:LOCALAPPDATA 'ASDC\LoadFile\NANOCAD'
$nanosoftRoot = Join-Path $env:APPDATA 'Nanosoft'

if (Get-Process nCad -ErrorAction SilentlyContinue) {
    throw 'nanoCAD запущен. Закройте nanoCAD и повторите деплой.'
}

if (-not (Test-Path $dll)) {
    throw "Не найден $dll`nСначала соберите: dotnet build `"$projDir\NcTestRibbon.csproj`" -c $Configuration"
}

if (-not (Test-Path $json)) {
    throw "Не найден манифест $json"
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item $dll (Join-Path $dest 'NC_TestRibbon.dll') -Force
Copy-Item $json (Join-Path $dest 'NC_TestRibbon.json') -Force

Write-Host "Скопировано в: $dest"
Write-Host '  - NC_TestRibbon.dll'
Write-Host '  - NC_TestRibbon.json'

function Get-NanoCadProfiles {
    $result = @()
    if (-not (Test-Path $nanosoftRoot)) { return $result }

    foreach ($dir in Get-ChildItem -Path $nanosoftRoot -Directory -ErrorAction SilentlyContinue) {
        if ($dir.Name -notmatch '(?i)^nanoCAD') { continue }
        $cfgIni = Join-Path $dir.FullName 'Config\Cfg.ini'
        if (Test-Path $cfgIni) {
            $result += [PSCustomObject]@{
                Name   = $dir.Name
                CfgIni = $cfgIni
            }
        }
    }
    return @($result | Sort-Object Name)
}

function Add-ManagedStartup {
    param(
        [Parameter(Mandatory = $true)][string]$CfgIni,
        [Parameter(Mandatory = $true)][string]$DllPath
    )

    $text = [IO.File]::ReadAllText($CfgIni)
    if ($text -like "*$DllPath*") {
        return $false
    }

    $used = @{}
    foreach ($m in [regex]::Matches($text, '\\Appload\\Startup\\app(\d+)\]')) {
        $used[[int]$m.Groups[1].Value] = $true
    }

    $n = 0
    while ($used.ContainsKey($n)) { $n++ }

    $section =
        "`r`n[\Configuration\<<Default>>\Appload\Startup\app$n]`r`n" +
        "Loader=s$DllPath`r`n" +
        "Type=sMGD`r`n" +
        "Enabled=i1`r`n"

    [IO.File]::AppendAllText(
        $CfgIni,
        $section,
        (New-Object System.Text.UTF8Encoding($false))
    )
    return $true
}

if ($RegisterStartup) {
    $profiles = @(Get-NanoCadProfiles)
    if ($profiles.Count -eq 0) {
        Write-Host ''
        Write-Host 'Профили nanoCAD с Cfg.ini не найдены.' -ForegroundColor Yellow
        Write-Host "Ожидался каталог: $nanosoftRoot"
        Write-Host 'Запустите nanoCAD один раз, закройте и повторите с -RegisterStartup.'
    }
    else {
        $dllInLoadFile = Join-Path $dest 'NC_TestRibbon.dll'
        Write-Host ''
        Write-Host ('Регистрация Startup в профилях: ' + $profiles.Count)
        foreach ($profile in $profiles) {
            $added = Add-ManagedStartup -CfgIni $profile.CfgIni -DllPath $dllInLoadFile
            if ($added) {
                Write-Host ("  + {0}: добавлено" -f $profile.Name)
            }
            else {
                Write-Host ("  = {0}: уже было" -f $profile.Name)
            }
        }
    }
}

Write-Host ''
Write-Host 'Готово. Дальше:'
Write-Host '1. Запустите nanoCAD.'
Write-Host '2. Если DLL зарегистрирована в Startup — команды доступны сразу:'
Write-Host '     NcTestHello'
Write-Host '     NcTestHttp'
Write-Host '3. Кнопки панели «Тест» на ленте появятся после обработки JSON через NcBootstrap'
Write-Host '   (или позже через ASDC). Без сервера команды можно вызывать с командной строки.'
Write-Host '4. После смены DLL/JSON перезапустите nanoCAD (лента/модули кэшируются).'
