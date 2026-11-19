# Print LoadFile DLL path / timestamps and check CommandMethod names are inside the DLL.
# ASCII-only. Does not start nanoCAD.

$ErrorActionPreference = 'Stop'

$required = @('NcTestHello', 'NcTestHttp', 'NcTestWpf', 'NcTestSettings', 'NcTestInitialData', 'NcTestPadOpt', 'NcTestBlink', 'NcTestEcw', 'NcTestTif', 'NcTestForest', 'NcTestHeight')
$loadDll = Join-Path $env:LOCALAPPDATA 'ASDC\LoadFile\NANOCAD\NC_TestRibbon.dll'
$loadJson = Join-Path $env:LOCALAPPDATA 'ASDC\LoadFile\NANOCAD\NC_TestRibbon.json'
$loadEpplus = Join-Path $env:LOCALAPPDATA 'ASDC\LoadFile\NANOCAD\EPPlus.dll'
$pkgRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$srcCommands = Join-Path $pkgRoot 'NcTestRibbon\Commands.cs'
$releaseDll = Join-Path $pkgRoot 'NcTestRibbon\bin\Release\net6.0-windows\NC_TestRibbon.dll'
$packagedEpplus = Join-Path $pkgRoot 'installer\deps\EPPlus.dll'

function Test-FileContainsText {
    param([string]$Path, [string]$Text)
    if (-not (Test-Path $Path)) { return $false }
    $bytes = [IO.File]::ReadAllBytes($Path)
    foreach ($enc in @([Text.Encoding]::Unicode, [Text.Encoding]::UTF8, [Text.Encoding]::ASCII)) {
        $needle = $enc.GetBytes($Text)
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

function Show-File {
    param([string]$Label, [string]$Path)
    Write-Host ''
    Write-Host ("=== {0} ===" -f $Label)
    if (-not (Test-Path $Path)) {
        Write-Host ("MISSING: {0}" -f $Path)
        return
    }
    $info = Get-Item $Path
    Write-Host ("Path:          {0}" -f $info.FullName)
    Write-Host ("LastWriteTime: {0}" -f $info.LastWriteTime)
    Write-Host ("Length:        {0} bytes" -f $info.Length)
    try {
        $fv = $info.VersionInfo.FileVersion
        $pv = $info.VersionInfo.ProductVersion
        if ($fv) { Write-Host ("FileVersion:   {0}" -f $fv) }
        if ($pv) { Write-Host ("ProductVersion:{0}" -f $pv) }
    } catch { }
}

Write-Host 'NcTestRibbon VERIFY'
Show-File -Label 'Installed LoadFile DLL' -Path $loadDll
Show-File -Label 'Installed LoadFile JSON' -Path $loadJson
Show-File -Label 'Installed LoadFile EPPlus' -Path $loadEpplus
Show-File -Label 'Packaged installer\deps EPPlus' -Path $packagedEpplus
Show-File -Label 'Local Release build DLL' -Path $releaseDll
Show-File -Label 'Source Commands.cs' -Path $srcCommands

Write-Host ''
Write-Host '=== CommandMethod names in source ==='
if (Test-Path $srcCommands) {
    Select-String -Path $srcCommands -Pattern 'CommandMethod\("([^"]+)"\)' |
        ForEach-Object { Write-Host ('  ' + $_.Matches[0].Groups[1].Value) }
}
else {
    Write-Host '  (Commands.cs not found next to installer package)'
}

Write-Host ''
Write-Host '=== Required names inside installed DLL ==='
$fail = $false
if (-not (Test-Path $loadDll)) {
    Write-Host 'Installed DLL missing. Run INSTALL.bat after a Release build.'
    $fail = $true
}
else {
    foreach ($cmd in $required) {
        $hit = Test-FileContainsText -Path $loadDll -Text $cmd
        if ($hit) {
            Write-Host ("  OK  {0}" -f $cmd)
        }
        else {
            Write-Host ("  MISSING  {0}" -f $cmd)
            $fail = $true
        }
    }
}

Write-Host ''
Write-Host '=== EPPlus next to plugin ==='
if (-not (Test-Path $packagedEpplus)) {
    Write-Host 'Packaged EPPlus.dll missing under installer\deps\.'
    $fail = $true
}
else {
    Write-Host '  OK  installer\deps\EPPlus.dll'
}
if (-not (Test-Path $loadEpplus)) {
    Write-Host 'Installed EPPlus.dll missing in LoadFile. Re-run INSTALL.bat.'
    $fail = $true
}
else {
    Write-Host '  OK  LoadFile\EPPlus.dll'
}

Write-Host ''
Write-Host 'In nanoCAD command line try:'
Write-Host '  NcTestHello'
Write-Host '  NcTestHttp'
Write-Host '  NcTestWpf'
Write-Host '  NcTestSettings'
Write-Host '  NcTestInitialData'
Write-Host '  NcTestPadOpt'
Write-Host '  NcTestBlink'
Write-Host '  NcTestBlinkOff'
Write-Host '  NcTestEcw'
Write-Host '  NcTestRaster'
Write-Host '  NcTestTif'
Write-Host '  NcTestForest'
Write-Host '  NcTestHeight'
Write-Host 'If unknown: rebuild Release, re-run INSTALL.bat, or NETLOAD the LoadFile DLL path above.'

Write-Host ''
Write-Host '=== Bundled GDAL ECW (NcTestEcw embedded georef) ==='
$gdalWrap = Join-Path $env:LOCALAPPDATA 'ASDC\LoadFile\NANOCAD\gdal\x64\gdal_wrap.dll'
$gdalEcw = Join-Path $env:LOCALAPPDATA 'ASDC\LoadFile\NANOCAD\gdal\x64\plugins\gdal_ECW_JP2ECW.dll'
$gdalNcs = Join-Path $env:LOCALAPPDATA 'ASDC\LoadFile\NANOCAD\gdal\x64\plugins\NCSEcw.dll'
$gdalCsharp = Join-Path $env:LOCALAPPDATA 'ASDC\LoadFile\NANOCAD\gdal_csharp.dll'
$ncGdalGeo = Join-Path $env:LOCALAPPDATA 'ASDC\LoadFile\NANOCAD\tools\NcGdalGeo\NcGdalGeo.exe'
foreach ($p in @($gdalWrap, $gdalEcw, $gdalNcs, $gdalCsharp, $ncGdalGeo)) {
    if (Test-Path $p) {
        Write-Host ("  OK  {0}" -f $p)
    }
    else {
        Write-Host ("  MISSING  {0}" -f $p)
        $fail = $true
    }
}

if ($fail) {
    Write-Host ''
    Write-Host 'VERIFY FAILED: rebuild and reinstall.' -ForegroundColor Yellow
    exit 1
}

Write-Host ''
Write-Host 'VERIFY OK: installed DLL contains all required command names.'
exit 0
