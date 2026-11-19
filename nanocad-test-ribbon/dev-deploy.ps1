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
    throw 'nanoCAD is running. Close it and retry.'
}

if (-not (Test-Path $dll)) {
    throw "DLL not found: $dll. Build first."
}
if (-not (Test-Path $json)) {
    throw "JSON not found: $json"
}

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item $dll (Join-Path $dest 'NC_TestRibbon.dll') -Force
Copy-Item $json (Join-Path $dest 'NC_TestRibbon.json') -Force

# Copy NuGet deps (EPPlus etc.) from build output and/or installer\deps.
$skip = @{
    'NC_TestRibbon.dll' = $true
    'hostmgd.dll'       = $true
    'hostdbmgd.dll'     = $true
}
$byName = @{}
$depsDir = Join-Path $root 'installer\deps'
if (Test-Path $depsDir) {
    Get-ChildItem $depsDir -Filter '*.dll' -File -ErrorAction SilentlyContinue |
        ForEach-Object { $byName[$_.Name] = $_.FullName }
}
$dllDir = Split-Path -Parent $dll
Get-ChildItem $dllDir -Filter '*.dll' -File -ErrorAction SilentlyContinue |
    Where-Object { -not $skip.ContainsKey($_.Name) } |
    ForEach-Object { $byName[$_.Name] = $_.FullName }
foreach ($name in @($byName.Keys)) {
    Copy-Item $byName[$name] (Join-Path $dest $name) -Force
}
Write-Host ("Copied to {0} (+ {1} dependency DLL(s))" -f $dest, $byName.Count)

function Get-NanoCadProfiles {
    $result = @()
    if (-not (Test-Path $nanosoftRoot)) { return $result }
    foreach ($dir in Get-ChildItem -Path $nanosoftRoot -Directory -ErrorAction SilentlyContinue) {
        if ($dir.Name -notmatch '(?i)^nanoCAD') { continue }
        $cfgIni = Join-Path $dir.FullName 'Config\Cfg.ini'
        if (Test-Path $cfgIni) {
            $result += [PSCustomObject]@{ Name = $dir.Name; CfgIni = $cfgIni }
        }
    }
    return @($result | Sort-Object Name)
}

function Add-ManagedStartup {
    param([string]$CfgIni, [string]$DllPath)
    $text = [IO.File]::ReadAllText($CfgIni)
    if ($text -like "*$DllPath*") { return $false }
    $used = @{}
    foreach ($m in [regex]::Matches($text, '\\Appload\\Startup\\app(\d+)\]')) {
        $used[[int]$m.Groups[1].Value] = $true
    }
    $n = 0
    while ($used.ContainsKey($n)) { $n++ }
    $section = "`r`n[\Configuration\<<Default>>\Appload\Startup\app$n]`r`n" +
               "Loader=s$DllPath`r`nType=sMGD`r`nEnabled=i1`r`n"
    [IO.File]::AppendAllText($CfgIni, $section, (New-Object System.Text.UTF8Encoding($false)))
    return $true
}

if ($RegisterStartup) {
    $profiles = @(Get-NanoCadProfiles)
    $dllInLoadFile = Join-Path $dest 'NC_TestRibbon.dll'
    Write-Host ("Profiles: " + $profiles.Count)
    foreach ($profile in $profiles) {
        $added = Add-ManagedStartup -CfgIni $profile.CfgIni -DllPath $dllInLoadFile
        if ($added) { Write-Host ("  + " + $profile.Name) } else { Write-Host ("  = " + $profile.Name + " already") }
    }
}

Write-Host 'Done. Start nanoCAD and run: NcTestHello / NcTestHttp / NcTestWpf'
