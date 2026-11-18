# Remove NcBootstrap + NC_TestRibbon Startup entries and local copies.
# Run with nanoCAD closed. ASCII-only script.

$ErrorActionPreference = 'Stop'

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

        if (Test-Path $configDir) {
            $result += [PSCustomObject]@{
                Name      = $dir.Name
                ConfigDir = $configDir
                CfgIni    = $cfgIni
                MasterCfg = Join-Path $configDir 'nanoCAD.cfg'
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
    $current = New-Object System.Collections.Generic.List[string]
    $result = New-Object System.Collections.Generic.List[string]

    function Flush-Current {
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
            Flush-Current $current $result
        }
        $current.Add($line)
    }

    Flush-Current $current $result

    [IO.File]::WriteAllLines(
        $CfgIni,
        $result,
        (New-Object System.Text.UTF8Encoding($true))
    )
}

Write-Host '=== Uninstall NcBootstrap + NC_TestRibbon ==='

if (Get-Process nCad -ErrorAction SilentlyContinue) {
    throw 'nanoCAD is running. Close all nanoCAD windows and retry.'
}

$profiles = @(Get-NanoCadProfiles)

foreach ($profile in $profiles) {
    Write-Host ''
    Write-Host ('--- ' + $profile.Name + ' ---')

    Remove-MatchingStartupSections -CfgIni $profile.CfgIni -Patterns @(
        'ASDC\\NcBootstrap',
        'NC_TestRibbon\.dll',
        'NC_RibbonExecDoc\.dll',
        'RibbonExecDoc\.dll'
    )

    if (Test-Path $profile.MasterCfg) {
        $kept = [IO.File]::ReadAllLines($profile.MasterCfg) | Where-Object {
            $_ -notmatch 'NcBootstrap\.cfg"' -and
            $_ -notmatch 'NC_TestRibbon\.cfg"' -and
            $_ -notmatch 'NC_RibbonExecDoc\.cfg"' -and
            $_ -notmatch 'RibbonExecDoc\.cfg"' -and
            $_ -notmatch 'NC_Tools\.cfg"'
        }

        [IO.File]::WriteAllLines(
            $profile.MasterCfg,
            $kept,
            (New-Object System.Text.UTF8Encoding($true))
        )
    }

    foreach ($f in @(
        'NcBootstrap.cfg', 'NcBootstrap.cuix',
        'NC_TestRibbon.cfg', 'NC_TestRibbon.cuix',
        'NC_RibbonExecDoc.cfg', 'NC_RibbonExecDoc.cuix',
        'RibbonExecDoc.cfg', 'RibbonExecDoc.cuix',
        'NC_Tools.cfg', 'NC_Tools.cuix'
    )) {
        $p = Join-Path $profile.ConfigDir $f
        if (Test-Path $p) {
            Remove-Item $p -Force
        }
    }

    Write-Host 'Profile Startup cleaned.'
}

if (Test-Path $dest) {
    Remove-Item $dest -Recurse -Force
    Write-Host "Removed: $dest"
}

foreach ($name in @(
    'NC_TestRibbon.dll', 'NC_TestRibbon.json',
    'NC_RibbonExecDoc.dll', 'NC_RibbonExecDoc.json', 'NC_RibbonExecDoc.pdb',
    'NC_Ribbon_Exec_Help.pdf',
    'RibbonExecDoc.dll', 'RibbonExecDoc.json'
)) {
    $p = Join-Path $syncFolder $name
    if (Test-Path $p) {
        Remove-Item $p -Force
        Write-Host "Removed: $p"
    }
}

Write-Host ''
Write-Host 'Uninstall complete.'
