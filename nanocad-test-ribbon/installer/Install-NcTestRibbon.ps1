# Install NcBootstrap + NC_TestRibbon for local nanoCAD testing.
# Run with nanoCAD closed. Admin rights are not required.
# ASCII-only script (safe across Windows code pages).

$ErrorActionPreference = 'Stop'

$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$pkgRoot = Split-Path -Parent $src
$dest = Join-Path $env:LOCALAPPDATA 'ASDC\NcBootstrap'
$syncFolder = Join-Path $env:LOCALAPPDATA 'ASDC\LoadFile\NANOCAD'
$nanosoftRoot = Join-Path $env:APPDATA 'Nanosoft'

function Get-NanoCadProfiles {
    $result = @()

    if (-not (Test-Path $nanosoftRoot)) {
        return $result
    }

    foreach ($dir in Get-ChildItem -Path $nanosoftRoot -Directory -ErrorAction SilentlyContinue) {
        if ($dir.Name -notmatch '(?i)^nanoCAD') {
            continue
        }

        $configDir = Join-Path $dir.FullName 'Config'
        $cfgIni = Join-Path $configDir 'Cfg.ini'

        if (Test-Path $cfgIni) {
            $result += [PSCustomObject]@{
                Name      = $dir.Name
                ConfigDir = $configDir
                CfgIni    = $cfgIni
            }
        }
    }

    return @($result | Sort-Object Name)
}

function Remove-MatchingStartupSections {
    param(
        [Parameter(Mandatory = $true)][string]$CfgIni,
        [Parameter(Mandatory = $true)][string[]]$Patterns
    )

    if (-not (Test-Path $CfgIni)) {
        return
    }

    $lines = [IO.File]::ReadAllLines($CfgIni)
    $outLines = New-Object System.Collections.Generic.List[string]
    $section = New-Object System.Collections.Generic.List[string]

    function Flush-Section {
        param(
            [System.Collections.Generic.List[string]]$buffer,
            [System.Collections.Generic.List[string]]$target
        )

        if ($buffer.Count -eq 0) { return }

        $joined = ($buffer -join "`n")
        $drop = $false
        foreach ($pattern in $Patterns) {
            if ($joined -match $pattern) {
                $drop = $true
                break
            }
        }

        if (-not $drop) {
            foreach ($line in $buffer) {
                $target.Add($line)
            }
        }

        $buffer.Clear()
    }

    foreach ($line in $lines) {
        if ($line.StartsWith('[')) {
            Flush-Section $section $outLines
        }
        $section.Add($line)
    }

    Flush-Section $section $outLines

    [IO.File]::WriteAllLines(
        $CfgIni,
        $outLines,
        (New-Object System.Text.UTF8Encoding($true))
    )
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
    while ($used.ContainsKey($n)) {
        $n++
    }

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

function Resolve-NanoCadExe {
    param(
        [object[]]$Profiles
    )

    $candidates = New-Object System.Collections.Generic.List[string]

    $searchRoots = @(
        ${env:ProgramFiles},
        ${env:ProgramFiles(x86)},
        ${env:ProgramW6432}
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique

    foreach ($root in $searchRoots) {
        foreach ($vendor in @('Nanosoft', 'Nanosoft AS')) {
            $vendorDir = Join-Path $root $vendor
            if (-not (Test-Path $vendorDir)) { continue }

            Get-ChildItem -Path $vendorDir -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '(?i)nanoCAD' } |
                ForEach-Object {
                    $exe = Join-Path $_.FullName 'nCad.exe'
                    if (Test-Path $exe) {
                        $candidates.Add($exe)
                    }
                }
        }
    }

    # Prefer exe whose folder name matches a known AppData profile (newest profile last).
    $profileNames = @($Profiles | ForEach-Object { $_.Name })
    foreach ($name in ($profileNames | Sort-Object -Descending)) {
        foreach ($exe in $candidates) {
            $parent = Split-Path -Parent $exe
            if ((Split-Path -Leaf $parent) -ieq $name) {
                return $exe
            }
        }
    }

    if ($candidates.Count -gt 0) {
        return @(
            $candidates |
                Sort-Object {
                    try { [version]((Get-Item $_).VersionInfo.ProductVersion -replace '[^\d\.]', '') }
                    catch { [version]'0.0.0' }
                } -Descending |
                Select-Object -First 1
        )[0]
    }

    return $null
}

function Stop-NanoCadProcesses {
    param(
        [int]$TimeoutSec = 30
    )

    $procs = @(Get-Process nCad -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { return }

    foreach ($p in $procs) {
        try { $p.CloseMainWindow() | Out-Null } catch { }
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $left = @(Get-Process nCad -ErrorAction SilentlyContinue)
        if ($left.Count -eq 0) { return }
        Start-Sleep -Seconds 1
    }

    Get-Process nCad -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

function Start-NanoCadAndWait {
    param(
        [Parameter(Mandatory = $true)][string]$ExePath,
        [int]$WaitSec = 25
    )

    Write-Host ("Starting: " + $ExePath)
    $proc = Start-Process -FilePath $ExePath -PassThru
    $deadline = (Get-Date).AddSeconds($WaitSec)

    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
        try {
            $proc.Refresh()
            if ($proc.HasExited) {
                Write-Host 'nanoCAD exited early.'
                return
            }
            if ($proc.MainWindowHandle -ne [IntPtr]::Zero) {
                Write-Host 'nanoCAD main window is up.'
                Start-Sleep -Seconds 5
                return
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    Write-Host ("Waited " + $WaitSec + "s for nanoCAD window; continuing.")
}

function Invoke-NanoCadDoubleLaunch {
    param(
        [Parameter(Mandatory = $true)][string]$ExePath
    )

    Write-Host ''
    Write-Host '=== Launch nanoCAD twice (ribbon tab warm-up) ==='
    Write-Host 'Pass 1: start, wait for UI, then close.'
    Start-NanoCadAndWait -ExePath $ExePath -WaitSec 25
    Stop-NanoCadProcesses -TimeoutSec 40
    Start-Sleep -Seconds 2

    Write-Host 'Pass 2: start again (leave running for you).'
    Start-NanoCadAndWait -ExePath $ExePath -WaitSec 25
    Write-Host 'nanoCAD should stay open. Check ribbon panel "Test".'
}

function Resolve-TestRibbonFiles {
    $candidates = @(
        @{
            Dll  = Join-Path $pkgRoot 'NcTestRibbon\bin\Release\net6.0-windows\NC_TestRibbon.dll'
            Json = Join-Path $pkgRoot 'NC_TestRibbon.json'
            Label = 'build output (Release)'
        },
        @{
            Dll  = Join-Path $pkgRoot 'NcTestRibbon\bin\Debug\net6.0-windows\NC_TestRibbon.dll'
            Json = Join-Path $pkgRoot 'NC_TestRibbon.json'
            Label = 'build output (Debug)'
        },
        @{
            Dll  = Join-Path $src 'payload\NC_TestRibbon.dll'
            Json = Join-Path $src 'payload\NC_TestRibbon.json'
            Label = 'installer\payload'
        },
        @{
            Dll  = Join-Path $pkgRoot 'payload\NC_TestRibbon.dll'
            Json = Join-Path $pkgRoot 'payload\NC_TestRibbon.json'
            Label = 'payload'
        }
    )

    foreach ($c in $candidates) {
        if ((Test-Path $c.Dll) -and (Test-Path $c.Json)) {
            return [PSCustomObject]@{
                Dll   = $c.Dll
                Json  = $c.Json
                Label = $c.Label
            }
        }
    }

    return $null
}

if (Get-Process nCad -ErrorAction SilentlyContinue) {
    throw 'nanoCAD is running. Close all nanoCAD windows and retry.'
}

foreach ($name in @('NcBootstrap.dll', 'NcBootstrap.json')) {
    $from = Join-Path $src $name
    if (-not (Test-Path $from)) {
        throw "Missing $from"
    }
}

$loadIconSrc = Join-Path $src 'icons\load_chart.ico'
if (-not (Test-Path $loadIconSrc)) {
    throw "Missing icon $loadIconSrc"
}

$ribbon = Resolve-TestRibbonFiles
if ($null -eq $ribbon) {
    throw @"
NC_TestRibbon.dll / NC_TestRibbon.json not found.
Build first:
  cd ..\NcTestRibbon
  dotnet build NcTestRibbon.csproj -c Release
Or place files into installer\payload\
"@
}

Write-Host '=== Install NcBootstrap + NC_TestRibbon ==='
Write-Host "Bootstrap source: $src"
Write-Host "Ribbon source:    $($ribbon.Label)"
Write-Host "  DLL:  $($ribbon.Dll)"
Write-Host "  JSON: $($ribbon.Json)"

# --- NcBootstrap ---
New-Item -ItemType Directory -Force -Path $dest | Out-Null
foreach ($name in @('NcBootstrap.dll', 'NcBootstrap.json', 'NcBootstrap.deps.json')) {
    $from = Join-Path $src $name
    if (Test-Path $from) {
        Copy-Item $from $dest -Force
    }
}

$iconsDest = Join-Path $dest 'icons'
if (Test-Path $iconsDest) {
    Remove-Item $iconsDest -Force -Recurse
}
New-Item -ItemType Directory -Force -Path $iconsDest | Out-Null
Copy-Item $loadIconSrc (Join-Path $iconsDest 'load_chart.ico') -Force
Write-Host "Copied NcBootstrap to: $dest"

# --- LoadFile: keep only NC_TestRibbon (strip Ramki / NC_RibbonExecDoc) ---
New-Item -ItemType Directory -Force -Path $syncFolder | Out-Null

$ramkiNames = @(
    'NC_RibbonExecDoc.dll',
    'NC_RibbonExecDoc.json',
    'NC_RibbonExecDoc.pdb',
    'NC_Ribbon_Exec_Help.pdf',
    'RibbonExecDoc.dll',
    'RibbonExecDoc.json'
)
foreach ($name in $ramkiNames) {
    $p = Join-Path $syncFolder $name
    if (Test-Path $p) {
        Remove-Item $p -Force
        Write-Host "Removed leftover: $p"
    }
}

# Never deploy NC_RibbonExecDoc even if present next to the installer.
foreach ($name in @('NC_RibbonExecDoc.dll', 'NC_RibbonExecDoc.json')) {
    $accidental = Join-Path $src $name
    if (Test-Path $accidental) {
        Write-Host "Skipping (not installing): $accidental"
    }
}

$ribbonDllDest = Join-Path $syncFolder 'NC_TestRibbon.dll'
$ribbonJsonDest = Join-Path $syncFolder 'NC_TestRibbon.json'
Copy-Item $ribbon.Dll $ribbonDllDest -Force
Copy-Item $ribbon.Json $ribbonJsonDest -Force
Write-Host "Copied NC_TestRibbon to: $syncFolder"

$profiles = @(Get-NanoCadProfiles)
if ($profiles.Count -eq 0) {
    Write-Host ''
    Write-Host 'No nanoCAD profiles with Cfg.ini found.' -ForegroundColor Yellow
    Write-Host "Checked: $nanosoftRoot"
    Write-Host 'Start the required nanoCAD version once, close it, then re-run INSTALL.bat.'
    throw 'No nanoCAD profile with Config\Cfg.ini was found.'
}

Write-Host ''
Write-Host ("nanoCAD profiles: " + $profiles.Count)
foreach ($profile in $profiles) {
    Write-Host ('  - ' + $profile.Name)
}

$bootstrapDll = Join-Path $dest 'NcBootstrap.dll'

foreach ($profile in $profiles) {
    Write-Host ''
    Write-Host ('--- ' + $profile.Name + ' ---')

    Remove-MatchingStartupSections -CfgIni $profile.CfgIni -Patterns @(
        'ASDC\\NcBootstrap',
        'NC_TestRibbon\.dll',
        'NC_RibbonExecDoc\.dll',
        'RibbonExecDoc\.dll'
    )

    $masterCfg = Join-Path $profile.ConfigDir 'nanoCAD.cfg'
    if (Test-Path $masterCfg) {
        $kept = [IO.File]::ReadAllLines($masterCfg) | Where-Object {
            $_ -notmatch 'NC_RibbonExecDoc\.cfg"' -and
            $_ -notmatch 'RibbonExecDoc\.cfg"'
        }
        [IO.File]::WriteAllLines(
            $masterCfg,
            $kept,
            (New-Object System.Text.UTF8Encoding($true))
        )
    }

    foreach ($f in @(
        'NC_RibbonExecDoc.cfg', 'NC_RibbonExecDoc.cuix',
        'RibbonExecDoc.cfg', 'RibbonExecDoc.cuix',
        'NC_Tools.cfg', 'NC_Tools.cuix'
    )) {
        $p = Join-Path $profile.ConfigDir $f
        if (Test-Path $p) {
            Remove-Item $p -Force
            Write-Host ("Removed profile UI cache: " + $f)
        }
    }

    $serverIcons = Join-Path $profile.ConfigDir 'server-icons'
    if (Test-Path $serverIcons) {
        Get-ChildItem $serverIcons -File -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -match '(?i)(number_list|export_pdf|delete_temp_sheets|RibbonExec|Ramki)'
            } |
            ForEach-Object { Remove-Item $_.FullName -Force }
    }

    $addedBootstrap = Add-ManagedStartup -CfgIni $profile.CfgIni -DllPath $bootstrapDll
    if ($addedBootstrap) {
        Write-Host 'Startup: NcBootstrap added.'
    }
    else {
        Write-Host 'Startup: NcBootstrap already present.'
    }

    $addedRibbon = Add-ManagedStartup -CfgIni $profile.CfgIni -DllPath $ribbonDllDest
    if ($addedRibbon) {
        Write-Host 'Startup: NC_TestRibbon added.'
    }
    else {
        Write-Host 'Startup: NC_TestRibbon already present.'
    }
}

Write-Host ''
Write-Host 'Install complete.'
Write-Host 'Only NC_TestRibbon (panel "Test") is installed. Ramki / NC_RibbonExecDoc is not deployed.'
Write-Host 'NcBootstrap.json has empty serverUrl (local-only; no ASDC sync of Ramki).'
Write-Host ''
Write-Host 'After reinstalling nanoCAD (before this installer):'
Write-Host '1. Start nanoCAD once, then close it (creates Config\Cfg.ini).'
Write-Host '2. Run installer\INSTALL.bat again.'

$nanoExe = Resolve-NanoCadExe -Profiles $profiles
if ($null -eq $nanoExe) {
    Write-Host ''
    Write-Host 'Could not find nCad.exe under Program Files\Nanosoft*.' -ForegroundColor Yellow
    Write-Host 'Start nanoCAD manually twice, then run: NcTestHello / NcTestHttp / NcTestWpf'
}
else {
    Write-Host ''
    Write-Host ("Resolved nanoCAD: " + $nanoExe)
    try {
        Invoke-NanoCadDoubleLaunch -ExePath $nanoExe
        Write-Host ''
        Write-Host 'Done. In nanoCAD run: NcTestHello / NcTestHttp / NcTestWpf'
    }
    catch {
        Write-Host ("Double-launch failed: " + $_.Exception.Message) -ForegroundColor Yellow
        Write-Host 'Start nanoCAD manually twice if the ribbon tab is missing.'
    }
}
