# Install NcBootstrap + NC_TestRibbon for local nanoCAD testing.
# Run with nanoCAD closed. Admin rights are not required.
# ASCII-only script (safe across Windows code pages).
#
# Flow (TCON-70):
#   1) dotnet build the plugin (failure => show log, exit, do NOT start nanoCAD)
#   2) deploy files + pre-write NC_Tools.cfg/.cuix + nanoCAD.cfg #include
#   3) start nanoCAD once (ribbon already wired; no ProposeRestart / second launch)

$ErrorActionPreference = 'Stop'

$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$pkgRoot = Split-Path -Parent $src
$dest = Join-Path $env:LOCALAPPDATA 'ASDC\NcBootstrap'
$syncFolder = Join-Path $env:LOCALAPPDATA 'ASDC\LoadFile\NANOCAD'
$nanosoftRoot = Join-Path $env:APPDATA 'Nanosoft'

. (Join-Path $src 'Write-NcToolsUi.ps1')

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

function Test-FileContainsText {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    if (-not (Test-Path $Path)) { return $false }

    $bytes = [IO.File]::ReadAllBytes($Path)
    $encodings = @(
        [Text.Encoding]::Unicode,   # UTF-16LE (typical .NET metadata strings)
        [Text.Encoding]::UTF8,
        [Text.Encoding]::ASCII
    )

    foreach ($enc in $encodings) {
        $needle = $enc.GetBytes($Text)
        if ($needle.Length -eq 0) { continue }
        for ($i = 0; $i -le $bytes.Length - $needle.Length; $i++) {
            $ok = $true
            for ($j = 0; $j -lt $needle.Length; $j++) {
                if ($bytes[$i + $j] -ne $needle[$j]) { $ok = $false; break }
            }
            if ($ok) { return $true }
        }
    }

    return $false
}

function Assert-DllHasCommands {
    param(
        [Parameter(Mandatory = $true)][string]$DllPath,
        [Parameter(Mandatory = $true)][string[]]$Commands
    )

    $missing = @()
    foreach ($cmd in $Commands) {
        if (-not (Test-FileContainsText -Path $DllPath -Text $cmd)) {
            $missing += $cmd
        }
    }

    if ($missing.Count -gt 0) {
        $info = Get-Item $DllPath
        throw @"
DLL is missing CommandMethod name(s): $($missing -join ', ')
File: $($info.FullName)
LastWriteTime: $($info.LastWriteTime)
Length: $($info.Length) bytes
This usually means a STALE build (old Release DLL) while NC_TestRibbon.json already lists new buttons.
Rebuild, then re-run INSTALL.bat:
  cd ..\NcTestRibbon
  dotnet build NcTestRibbon.csproj -c Release
Required commands: $($Commands -join ', ')
"@
    }
}

function Write-DllInfo {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (-not (Test-Path $Path)) {
        Write-Host ("{0}: MISSING {1}" -f $Label, $Path)
        return
    }

    $info = Get-Item $Path
    $ver = $null
    try { $ver = $info.VersionInfo.FileVersion } catch { }
    Write-Host ("{0}: {1}" -f $Label, $info.FullName)
    Write-Host ("  LastWriteTime: {0}" -f $info.LastWriteTime)
    Write-Host ("  Length:        {0} bytes" -f $info.Length)
    if ($ver) { Write-Host ("  FileVersion:   {0}" -f $ver) }
}

function Resolve-TestRibbonFiles {
    # Prefer package-root JSON so ribbon always matches this checkout.
    $pkgJson = Join-Path $pkgRoot 'NC_TestRibbon.json'

    $candidates = @(
        @{
            Dll  = Join-Path $pkgRoot 'NcTestRibbon\bin\Release\net6.0-windows\NC_TestRibbon.dll'
            Json = $pkgJson
            Label = 'build output (Release)'
        },
        @{
            Dll  = Join-Path $pkgRoot 'NcTestRibbon\bin\Debug\net6.0-windows\NC_TestRibbon.dll'
            Json = $pkgJson
            Label = 'build output (Debug)'
        },
        @{
            Dll  = Join-Path $src 'payload\NC_TestRibbon.dll'
            Json = $(if (Test-Path $pkgJson) { $pkgJson } else { Join-Path $src 'payload\NC_TestRibbon.json' })
            Label = 'installer\payload'
        },
        @{
            Dll  = Join-Path $pkgRoot 'payload\NC_TestRibbon.dll'
            Json = $(if (Test-Path $pkgJson) { $pkgJson } else { Join-Path $pkgRoot 'payload\NC_TestRibbon.json' })
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

function Get-RibbonDependencyDlls {
    param(
        [Parameter(Mandatory = $true)][string]$RibbonDllPath
    )

    $skip = @{
        'NC_TestRibbon.dll' = $true
        'hostmgd.dll'       = $true
        'hostdbmgd.dll'     = $true
        'NcBootstrap.dll'   = $true
    }

    $byName = @{}

    $depsDir = Join-Path $src 'deps'
    if (Test-Path $depsDir) {
        Get-ChildItem -Path $depsDir -Filter '*.dll' -File -ErrorAction SilentlyContinue |
            ForEach-Object { $byName[$_.Name] = $_.FullName }
    }

    $ribbonDir = Split-Path -Parent $RibbonDllPath
    if (Test-Path $ribbonDir) {
        Get-ChildItem -Path $ribbonDir -Filter '*.dll' -File -ErrorAction SilentlyContinue |
            Where-Object { -not $skip.ContainsKey($_.Name) } |
            ForEach-Object { $byName[$_.Name] = $_.FullName }
    }

    $payloadDeps = Join-Path $src 'payload'
    if (Test-Path $payloadDeps) {
        Get-ChildItem -Path $payloadDeps -Filter '*.dll' -File -ErrorAction SilentlyContinue |
            Where-Object { -not $skip.ContainsKey($_.Name) } |
            ForEach-Object {
                if (-not $byName.ContainsKey($_.Name)) {
                    $byName[$_.Name] = $_.FullName
                }
            }
    }

    return @(
        $byName.GetEnumerator() |
            Sort-Object Name |
            ForEach-Object {
                [PSCustomObject]@{ Name = $_.Key; Path = $_.Value }
            }
    )
}

function Copy-RibbonDependencies {
    param(
        [Parameter(Mandatory = $true)][string]$RibbonDllPath,
        [Parameter(Mandatory = $true)][string]$DestFolder
    )

    $deps = @(Get-RibbonDependencyDlls -RibbonDllPath $RibbonDllPath)
    if ($deps.Count -eq 0) {
        Write-Host 'WARNING: no plugin dependency DLLs found (installer\deps or build output).' -ForegroundColor Yellow
        Write-Host 'EPPlus.dll must sit next to NC_TestRibbon.dll for Price.xlsx rates.'
        return
    }

    $copied = 0
    foreach ($dep in $deps) {
        $destPath = Join-Path $DestFolder $dep.Name
        if (Test-Path $destPath) {
            Remove-Item $destPath -Force
        }
        Copy-Item $dep.Path $destPath -Force
        $copied++
    }

    Write-Host ("Copied {0} dependency DLL(s) to: {1}" -f $copied, $DestFolder)
    $epplus = Join-Path $DestFolder 'EPPlus.dll'
    if (Test-Path $epplus) {
        Write-DllInfo -Label 'Installed EPPlus' -Path $epplus
    }
    else {
        Write-Host 'WARNING: EPPlus.dll was not installed. Rates tab will fail to load.' -ForegroundColor Yellow
    }
}

$requiredCommands = @(
    'NcTestHello', 'NcTestHttp', 'NcTestWpf', 'NcTestSettings',
    'NcTestSourceData', 'NcTestAddPad', 'NcTestAddBuos', 'NcTestAddNode',
    'NcTestSaveSources', 'NcTestLoadSources',
    'NcTestRouteLine', 'NcTestRoad', 'NcTestSendToServer',
    'NcTestInterface', 'NcTestBlink',
    'NcTestEcw', 'NcTestCartography', 'NcTestAllowableSites',
    'NcTestTif', 'NcTestForest', 'NcTestHeight'
)

function Invoke-DotNetBuild {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$LogPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Write-Host ''
    Write-Host ("=== Building {0} (Release) ===" -f $Label)
    Write-Host ("Project: {0}" -f $ProjectPath)
    Write-Host ("Log:     {0}" -f $LogPath)

    $output = & dotnet build $ProjectPath -c Release --nologo 2>&1
    $exit = $LASTEXITCODE
    $output | Tee-Object -FilePath $LogPath | ForEach-Object { Write-Host $_ }

    if ($exit -ne 0) {
        Write-Host ''
        Write-Host 'BUILD FAILED - nanoCAD will NOT be started.' -ForegroundColor Red
        Write-Host ("Full log: {0}" -f $LogPath)
        Write-Host '--- last 60 log lines ---'
        Get-Content -LiteralPath $LogPath -Tail 60 -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_ }
        throw ("dotnet build failed for {0} (exit {1}). Fix compile errors and re-run INSTALL.bat." -f $Label, $exit)
    }
}

function Invoke-PluginBuild {
    $proj = Join-Path $pkgRoot 'NcTestRibbon\NcTestRibbon.csproj'
    if (-not (Test-Path -LiteralPath $proj)) {
        throw "Missing project: $proj"
    }

    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        throw 'dotnet SDK not found on PATH. Install .NET 6+ SDK, then re-run INSTALL.bat. nanoCAD was NOT started.'
    }

    $bootstrapProj = Join-Path $pkgRoot 'NcBootstrap\NcBootstrap.csproj'
    if (Test-Path -LiteralPath $bootstrapProj) {
        $bootLog = Join-Path $env:TEMP 'NcBootstrap-build.log'
        Invoke-DotNetBuild -ProjectPath $bootstrapProj -LogPath $bootLog -Label 'NcBootstrap'
        $bootDll = Join-Path $pkgRoot 'NcBootstrap\bin\Release\net6.0-windows\NcBootstrap.dll'
        if (-not (Test-Path -LiteralPath $bootDll)) {
            throw "Build reported success but DLL missing: $bootDll"
        }
        Copy-Item -LiteralPath $bootDll -Destination (Join-Path $src 'NcBootstrap.dll') -Force
        $bootDeps = Join-Path $pkgRoot 'NcBootstrap\bin\Release\net6.0-windows\NcBootstrap.deps.json'
        if (Test-Path -LiteralPath $bootDeps) {
            Copy-Item -LiteralPath $bootDeps -Destination (Join-Path $src 'NcBootstrap.deps.json') -Force
        }
        Write-Host 'NcBootstrap DLL copied into installer\.'
        Write-DllInfo -Label 'Built NcBootstrap' -Path $bootDll
    }
    else {
        Write-Host 'WARNING: NcBootstrap.csproj missing; using prebuilt installer\NcBootstrap.dll' -ForegroundColor Yellow
    }

    $log = Join-Path $env:TEMP 'NcTestRibbon-build.log'
    Invoke-DotNetBuild -ProjectPath $proj -LogPath $log -Label 'NC_TestRibbon'

    $builtDll = Join-Path $pkgRoot 'NcTestRibbon\bin\Release\net6.0-windows\NC_TestRibbon.dll'
    if (-not (Test-Path -LiteralPath $builtDll)) {
        throw "Build reported success but DLL missing: $builtDll"
    }

    Write-Host 'Build OK.'
    Write-DllInfo -Label 'Built DLL' -Path $builtDll
}

function Find-NanoCadExe {
    param([object[]]$Profiles)

    $candidates = New-Object System.Collections.Generic.List[string]
    foreach ($pf in @(${env:ProgramFiles}, ${env:ProgramFiles(x86)}, $env:ProgramW6432)) {
        if ([string]::IsNullOrWhiteSpace($pf)) { continue }
        $ns = Join-Path $pf 'Nanosoft'
        if (-not (Test-Path -LiteralPath $ns)) { continue }
        Get-ChildItem -Path $ns -Directory -ErrorAction SilentlyContinue | ForEach-Object {
            $exe = Join-Path $_.FullName 'nCad.exe'
            if (Test-Path -LiteralPath $exe) {
                $candidates.Add($exe)
            }
        }
    }

    if ($candidates.Count -eq 0) { return $null }

    foreach ($profile in @($Profiles)) {
        $name = [string]$profile.Name
        foreach ($exe in $candidates) {
            $parent = Split-Path -Parent $exe
            if ([string]::Equals((Split-Path -Leaf $parent), $name, [StringComparison]::OrdinalIgnoreCase)) {
                return $exe
            }
        }
    }

    return ($candidates | Sort-Object { (Get-Item $_).LastWriteTime } -Descending | Select-Object -First 1)
}

function Start-NanoCadOnce {
    param([object[]]$Profiles)

    $exe = Find-NanoCadExe -Profiles $Profiles
    if (-not $exe) {
        Write-Host 'WARNING: nCad.exe not found under Program Files\Nanosoft. Start nanoCAD manually.' -ForegroundColor Yellow
        return $false
    }

    Write-Host ''
    Write-Host ("Starting nanoCAD once: {0}" -f $exe)
    Start-Process -FilePath $exe | Out-Null
    return $true
}

# Leftovers from unzip-over-old-folder cause CS0101/CS0111 (duplicate classes).
# Keep Page pairs MainWindow/ProfileControl/RatesPanel *.xaml.cs - purge the rest.
$shellDir = Join-Path $pkgRoot 'NcTestRibbon\Shell'
foreach ($legacy in @(
        'SettingsWindow.xaml',
        'SettingsWindow.xaml.cs',
        'TracingSettingsPanel.xaml.cs',
        'InitialData.xaml',
        'InitialData.xaml.cs',
        'InitialData.cs',
        'PadOptimizationWindow.xaml',
        'PadOptimizationWindow.xaml.cs',
        'TrenchRoadSettingsPanel.xaml',
        'TrenchRoadSettingsPanel.xaml.cs'
    )) {
    $legacyPath = Join-Path $shellDir $legacy
    if (Test-Path $legacyPath) {
        Remove-Item -LiteralPath $legacyPath -Force
        Write-Host "Removed leftover: $legacyPath"
    }
}
$projDir = Join-Path $pkgRoot 'NcTestRibbon'
foreach ($legacy in @('DemoWindow.xaml', 'DemoWindow.xaml.cs', 'DemoWindow.cs')) {
    $legacyPath = Join-Path $projDir $legacy
    if (Test-Path $legacyPath) {
        Remove-Item -LiteralPath $legacyPath -Force
        Write-Host "Removed leftover: $legacyPath"
    }
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

# Compile first. Failure exits before any nanoCAD launch.
Invoke-PluginBuild

$ribbon = Resolve-TestRibbonFiles
if ($null -eq $ribbon) {
    throw @"
NC_TestRibbon.dll / NC_TestRibbon.json not found after build.
Expected: NcTestRibbon\bin\Release\net6.0-windows\NC_TestRibbon.dll
Or place files into installer\payload\
"@
}

Write-Host '=== Install NcBootstrap + NC_TestRibbon ==='
Write-Host "Bootstrap source: $src"
Write-Host "Ribbon source:    $($ribbon.Label)"
Write-DllInfo -Label 'Source DLL' -Path $ribbon.Dll
Write-Host "  JSON: $($ribbon.Json)"

Assert-DllHasCommands -DllPath $ribbon.Dll -Commands $requiredCommands
Write-Host ("DLL contains commands: " + ($requiredCommands -join ', '))

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

# Force overwrite (delete first so a locked/stale file fails loudly).
foreach ($p in @($ribbonDllDest, $ribbonJsonDest)) {
    if (Test-Path $p) {
        Remove-Item $p -Force
    }
}
Copy-Item $ribbon.Dll $ribbonDllDest -Force
Copy-Item $ribbon.Json $ribbonJsonDest -Force
Assert-DllHasCommands -DllPath $ribbonDllDest -Commands $requiredCommands
Write-Host "Copied NC_TestRibbon to: $syncFolder"
Write-DllInfo -Label 'Installed DLL' -Path $ribbonDllDest

# EPPlus + transitive NuGet deps must sit next to the plugin in LoadFile.
Copy-RibbonDependencies -RibbonDllPath $ribbon.Dll -DestFolder $syncFolder

# GDAL runtime (natives + NcGdalGeo): optional in slim plugin zips.
# Prefer package copy when present; else reuse existing LoadFile from v1.2.48 / gdal-runtime zip.
$gdalSrc = Join-Path $src 'gdal'
if (-not (Test-Path (Join-Path $gdalSrc 'x64\gdal_wrap.dll'))) {
    $gdalSrc = Join-Path $pkgRoot 'installer\gdal'
}
$gdalDest = Join-Path $syncFolder 'gdal'
$wrap = Join-Path $gdalDest 'x64\gdal_wrap.dll'
$ecw = Join-Path $gdalDest 'x64\plugins\gdal_ECW_JP2ECW.dll'
$ncs = Join-Path $gdalDest 'x64\plugins\NCSEcw.dll'
$csharp = Join-Path $syncFolder 'gdal_csharp.dll'

if (Test-Path (Join-Path $gdalSrc 'x64\gdal_wrap.dll')) {
    Write-Host ("GDAL source: {0}" -f $gdalSrc)
    if (Test-Path $gdalDest) {
        Remove-Item $gdalDest -Recurse -Force
    }
    New-Item -ItemType Directory -Path $gdalDest -Force | Out-Null
    Copy-Item -Path (Join-Path $gdalSrc '*') -Destination $gdalDest -Recurse -Force
    Write-Host "Copied bundled GDAL+ECW to: $gdalDest"
}
elseif ((Test-Path $wrap) -and (Test-Path $ecw) -and (Test-Path $ncs)) {
    Write-Host ("Reusing existing LoadFile GDAL: {0}" -f $gdalDest)
}
else {
    Write-Host 'ERROR: GDAL runtime missing in package AND LoadFile.' -ForegroundColor Yellow
    Write-Host 'Install once: nanocad-gdal-runtime-3.12.1.zip (or keep LoadFile\gdal from v1.2.48).'
    Write-Host '  Release: https://github.com/rublikdimas1989/Nanocad-Ribbon/releases/tag/gdal-runtime-3.12.1'
    throw 'GDAL runtime not found. Slim plugin zip reuses LoadFile\gdal from 1.2.48 / gdal-runtime asset.'
}

Write-Host ("  wrap:   {0}  exists={1}" -f $wrap, (Test-Path $wrap))
Write-Host ("  ECW:    {0}  exists={1}" -f $ecw, (Test-Path $ecw))
Write-Host ("  NCSEcw: {0}  exists={1}" -f $ncs, (Test-Path $ncs))
Write-Host ("  csharp: {0}  exists={1}" -f $csharp, (Test-Path $csharp))
if (-not ((Test-Path $wrap) -and (Test-Path $ecw) -and (Test-Path $ncs))) {
    throw 'GDAL incomplete (need wrap + ECW plugins).'
}

$toolSrc = Join-Path $src 'tools\NcGdalGeo'
if (-not (Test-Path (Join-Path $toolSrc 'NcGdalGeo.exe'))) {
    $toolSrc = Join-Path $pkgRoot 'installer\tools\NcGdalGeo'
}
$toolDest = Join-Path $syncFolder 'tools\NcGdalGeo'
$toolExe = Join-Path $toolDest 'NcGdalGeo.exe'
if (Test-Path (Join-Path $toolSrc 'NcGdalGeo.exe')) {
    if (Test-Path $toolDest) {
        Remove-Item $toolDest -Recurse -Force
    }
    New-Item -ItemType Directory -Path $toolDest -Force | Out-Null
    Copy-Item -Path (Join-Path $toolSrc '*') -Destination $toolDest -Recurse -Force
    Write-Host ("NcGdalGeo copied: {0}" -f $toolExe)
}
elseif (Test-Path $toolExe) {
    Write-Host ("Reusing existing NcGdalGeo: {0}" -f $toolExe)
}
else {
    Write-Host 'ERROR: NcGdalGeo missing in package AND LoadFile.' -ForegroundColor Yellow
    Write-Host 'Install nanocad-gdal-runtime-3.12.1.zip (includes tools\NcGdalGeo) once.'
    throw 'NcGdalGeo.exe not found. Slim plugin zip reuses LoadFile tools from 1.2.48 / gdal-runtime.'
}

# Seed ECW OEM encode example (optional; without it NcTestTif uses free Maps\*.tif).
$encodeExampleSrc = Join-Path $src 'ecw_encode.conf.example'
if (-not (Test-Path $encodeExampleSrc)) {
    $encodeExampleSrc = Join-Path $pkgRoot 'installer\ecw_encode.conf.example'
}
$settingsDir = Join-Path $syncFolder 'Tracing_cache\Settings'
if (Test-Path $encodeExampleSrc) {
    New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
    $encodeExampleDest = Join-Path $settingsDir 'ecw_encode.conf.example'
    Copy-Item $encodeExampleSrc $encodeExampleDest -Force
    Write-Host ("ECW encode example: {0}" -f $encodeExampleDest)
}


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
        'RibbonExecDoc.cfg', 'RibbonExecDoc.cuix'
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

    # Pre-generate ribbon UI so the first nanoCAD start already shows the tab.
    # (Previously INSTALL deleted NC_Tools.* and relied on Initialize + restart.)
    $pluginIconsDir = Join-Path $pkgRoot 'NcTestRibbon\icons'
    Write-NcToolsUi `
        -ConfigDir $profile.ConfigDir `
        -ManifestJsonPath $ribbon.Json `
        -SyncFolder $syncFolder `
        -LoadIconSource $loadIconSrc `
        -PluginIconsDir $pluginIconsDir
}

Write-Host ''
Write-Host 'Install complete.'
Write-Host 'Only NC_TestRibbon (panel "Test") is installed. Ramki / NC_RibbonExecDoc is not deployed.'
Write-Host 'NcBootstrap.json has empty serverUrl (local-only; no ASDC sync of Ramki).'
Write-Host 'Ribbon UI (NC_Tools.cfg/.cuix + nanoCAD.cfg include) was written before launch.'
Write-Host 'Optional check: installer\VERIFY.bat (DLL path / FileVersion / command names).'
Write-Host ''

$started = Start-NanoCadOnce -Profiles $profiles
if ($started) {
    Write-Host 'nanoCAD started once with ribbon pre-installed. No second launch required.'
}
else {
    Write-Host 'Install OK, but nanoCAD was not auto-started (nCad.exe not found). Start it manually once.'
}
