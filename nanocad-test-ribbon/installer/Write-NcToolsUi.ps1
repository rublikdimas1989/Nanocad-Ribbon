# Pre-generate NC_Tools.cfg / NC_Tools.cuix to match NcBootstrap.UiGenerator.
# Call before the first nanoCAD start so the ribbon is visible without a restart.
# ASCII-only script body; Russian strings are UTF-8 in data.

function Get-NcSanitizeId {
    param([string]$Value)
    if ($null -eq $Value) { $Value = '' }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $Value.ToCharArray()) {
        if ([char]::IsLetterOrDigit($ch)) {
            [void]$sb.Append($ch)
        }
        else {
            [void]$sb.Append('_')
        }
    }
    return $sb.ToString()
}

function Get-NcXmlEscape {
    param([string]$Value)
    if ($null -eq $Value) { $Value = '' }
    return $Value.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;')
}

function Get-NcRibbonButtonXml {
    param(
        [string]$Uid,
        [string]$Text,
        [string]$Command,
        [string]$Style,
        [string]$Tooltip
    )

    $buttonStyle = if ($Style -eq 'small') { 'SmallWithText' } else { 'LargeWithText' }
    $t = Get-NcXmlEscape $Text
    $tip = Get-NcXmlEscape $Tooltip
    return @(
        "          <RibbonCommandButton UID=`"$Uid`" Id=`"AcRibbonCommandButton`" Text=`"$t`" ButtonStyle=`"$buttonStyle`" MenuMacroID=`"$Command`" KeyTip=`"`">"
        "            <TooltipTitle xlate=`"true`" UID=`"XLS_$Uid`">$tip</TooltipTitle>"
        "          </RibbonCommandButton>"
    ) -join "`n"
}

# nanoCAD / AutoCAD-style ribbon drop-down (like Dimensions flyout): icon+label rows.
# Build XML via expansion (same as Get-NcRibbonButtonXml) — never -f / String.Format,
# so literal braces in CUI markup cannot throw FormatError.
function Get-NcRibbonFlyoutXml {
    param(
        [string]$Uid,
        [string]$Text,
        [string]$Style,
        [object[]]$Items
    )

    $buttonStyle = if ($Style -eq 'small') { 'SmallWithText' } else { 'LargeWithText' }
    $t = Get-NcXmlEscape $Text
    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add(
        ("          <RibbonSplitButton UID=`"$Uid`" Id=`"AcRibbonSplitButton`" Text=`"$t`" " +
         "Behavior=`"DropDown`" ListStyle=`"IconText`" ButtonStyle=`"$buttonStyle`" Grouping=`"false`" KeyTip=`"`">")
    )
    [void]$lines.Add("            <TooltipTitle xlate=`"true`" UID=`"XLS_$Uid`">$t</TooltipTitle>")

    $i = 0
    foreach ($item in @($Items)) {
        $i++
        $cmd = [string]$item.command
        $itemText = [string]$item.text
        if ([string]::IsNullOrWhiteSpace($cmd) -or [string]::IsNullOrWhiteSpace($itemText)) { continue }
        $childUid = $Uid + '_I' + $i + '_' + (Get-NcSanitizeId $cmd)
        $et = Get-NcXmlEscape $itemText
        [void]$lines.Add(
            ("            <RibbonCommandButton UID=`"$childUid`" Id=`"AcRibbonCommandButton`" Text=`"$et`" " +
             "ButtonStyle=`"SmallWithText`" MenuMacroID=`"$cmd`" KeyTip=`"`">")
        )
        [void]$lines.Add("              <TooltipTitle xlate=`"true`" UID=`"XLS_$childUid`">$et</TooltipTitle>")
        [void]$lines.Add('            </RibbonCommandButton>')
    }

    [void]$lines.Add('          </RibbonSplitButton>')
    return ($lines -join "`n")
}

function Resolve-NcIconSourceFile {
    param(
        [Parameter(Mandatory = $true)][string]$IconResource,
        [Parameter(Mandatory = $true)][string]$IconsDir
    )

    if ([string]::IsNullOrWhiteSpace($IconResource)) { return $null }

    # LogicalName NC_ICONS.hello.ico -> icons\hello.ico
    if ($IconResource -match '^NC_ICONS\.(.+)$') {
        $name = $Matches[1]
        $candidate = Join-Path $IconsDir $name
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }

    $direct = Join-Path $IconsDir $IconResource
    if (Test-Path -LiteralPath $direct) { return $direct }

    return $null
}

function Write-NcToolsUi {
    param(
        [Parameter(Mandatory = $true)][string]$ConfigDir,
        [Parameter(Mandatory = $true)][string]$ManifestJsonPath,
        [Parameter(Mandatory = $true)][string]$SyncFolder,
        [Parameter(Mandatory = $true)][string]$LoadIconSource,
        [Parameter(Mandatory = $true)][string]$PluginIconsDir
    )

    if (-not (Test-Path -LiteralPath $ManifestJsonPath)) {
        throw "Ribbon manifest not found: $ManifestJsonPath"
    }
    if (-not (Test-Path -LiteralPath $LoadIconSource)) {
        throw "Load icon not found: $LoadIconSource"
    }

    $manifest = Get-Content -LiteralPath $ManifestJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    # Prefer panels[] (multi-group ribbon). Legacy: single panel + buttons.
    $panelList = @()
    if ($null -ne $manifest.panels) {
        $panelList = @($manifest.panels)
    }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$manifest.panel)) {
        $panelList = @(@{
                name    = [string]$manifest.panel
                buttons = @($manifest.buttons)
            })
    }
    if ($panelList.Count -eq 0) {
        throw "Invalid ribbon manifest (missing panels[] or panel): $ManifestJsonPath"
    }

    New-Item -ItemType Directory -Force -Path $ConfigDir | Out-Null

    # --- load icon (NcBootstrap CopyLoadIcon) ---
    $iconsDest = Join-Path $ConfigDir 'icons'
    New-Item -ItemType Directory -Force -Path $iconsDest | Out-Null
    Copy-Item -LiteralPath $LoadIconSource -Destination (Join-Path $iconsDest 'load_chart.ico') -Force
    $loadIconRel = 'icons\load_chart.ico'

    $serverIcons = Join-Path $ConfigDir 'server-icons'
    New-Item -ItemType Directory -Force -Path $serverIcons | Out-Null

    # --- NC_Tools.cfg (single backslashes - match NcBootstrap.UiGenerator) ---
    $cfg = New-Object System.Text.StringBuilder
    [void]$cfg.AppendLine('[\configman]')
    [void]$cfg.AppendLine('[\configman\commands]')
    [void]$cfg.AppendLine()
    # ASCII-only labels in .ps1 (Windows PowerShell 5 / code-page safe).
    [void]$cfg.AppendLine('[\menu\NcTools]                          |name=sNC Tools')
    [void]$cfg.AppendLine('[\menu\NcTools\NcLoad]                   |name=sLoad instruments |intername=sNCLOAD')
    [void]$cfg.AppendLine('[\menu\NcTools\NcUnload]                 |name=sUnload instruments |intername=sNCUNLOAD')
    [void]$cfg.AppendLine()
    [void]$cfg.AppendLine('[\toolbars\NcTools]                      |name=sNC Tools |InitialVisible=f1')
    [void]$cfg.AppendLine('[\toolbars\NcTools\NcLoad]               |intername=sNCLOAD |ToolTipText=sDownload and load instruments from server')
    [void]$cfg.AppendLine('[\toolbars\NcTools\sep0]')
    [void]$cfg.AppendLine('[\toolbars\NcTools\NcUnload]             |intername=sNCUNLOAD |ToolTipText=sRemove loaded instruments')
    [void]$cfg.AppendLine()
    [void]$cfg.AppendLine('[\toolbarspos\NcTools]                   |DockPosition=sTop |row=i5 |pos=i0')
    [void]$cfg.AppendLine()
    [void]$cfg.AppendLine('[\ribbon\NcTools]')
    [void]$cfg.AppendLine('CUIX=s%CFG_PATH%\NC_Tools.cuix')
    [void]$cfg.AppendLine('visible=f1')
    [void]$cfg.AppendLine()

    function Append-NcCommand {
        param(
            [System.Text.StringBuilder]$Builder,
            [string]$InterName,
            [string]$DispName,
            [string]$Tooltip,
            [string]$IconPath
        )
        [void]$Builder.AppendLine("[\configman\commands\$InterName]")
        [void]$Builder.AppendLine('weight=i30')
        [void]$Builder.AppendLine('cmdtype=i1')
        [void]$Builder.AppendLine("intername=s$InterName")
        if (-not [string]::IsNullOrWhiteSpace($IconPath)) {
            [void]$Builder.AppendLine("BitmapDll=s$IconPath")
        }
        [void]$Builder.AppendLine("DispName=s$DispName")
        [void]$Builder.AppendLine("ToolTipText=s$Tooltip")
        [void]$Builder.AppendLine("StatusText=s$Tooltip")
        [void]$Builder.AppendLine()
    }

    Append-NcCommand $cfg 'NCLOAD' 'Load instruments' 'Download and load instruments from server' $loadIconRel
    Append-NcCommand $cfg 'NCUNLOAD' 'Unload instruments' 'Remove loaded instruments (after restart)' $null
    Append-NcCommand $cfg 'NCDIAG' 'NcBootstrap diagnostics' 'NcBootstrap setup info' $null

    $commands = @($manifest.commands)
    foreach ($cmd in $commands) {
        $iconRel = $null
        $asm = [string]$cmd.iconAssembly
        $res = [string]$cmd.iconResource
        $inter = [string]$cmd.intername
        if (-not [string]::IsNullOrWhiteSpace($asm) -and -not [string]::IsNullOrWhiteSpace($res) -and -not [string]::IsNullOrWhiteSpace($inter)) {
            $srcIcon = Resolve-NcIconSourceFile -IconResource $res -IconsDir $PluginIconsDir
            if ($srcIcon) {
                $dllBase = [IO.Path]::GetFileNameWithoutExtension($asm)
                $outName = $dllBase + '_' + (Get-NcSanitizeId $inter) + '.ico'
                $outPath = Join-Path $serverIcons $outName
                Copy-Item -LiteralPath $srcIcon -Destination $outPath -Force
                $iconRel = 'server-icons\' + $outName
            }
        }
        Append-NcCommand $cfg $inter ([string]$cmd.displayName) ([string]$cmd.tooltip) $iconRel
    }

    $cfgPath = Join-Path $ConfigDir 'NC_Tools.cfg'
    [IO.File]::WriteAllText($cfgPath, $cfg.ToString(), (New-Object System.Text.UTF8Encoding($true)))

    # --- NC_Tools.cuix (zip: RibbonRoot.cui + Menu_Package_Info.xml + [Content_Types].xml) ---
    $panels = New-Object System.Text.StringBuilder
    $refs = New-Object System.Text.StringBuilder

    [void]$panels.Append("    <RibbonPanelSource UID=`"RBNU_NCTOOLS_CTRL`" Text=`"Control`" HiddenInEditor=`"false`">`n")
    [void]$panels.Append("      <RibbonRowPanel UID=`"RBNURP_NCTOOLS_CTRL`" ResizeStyle=`"None`" ResizePriority=`"100`" TopJustify=`"True`">`n")
    [void]$panels.Append("        <RibbonRow UID=`"RBNURR_NCTOOLS_CTRL`">`n")
    [void]$panels.Append((Get-NcRibbonButtonXml -Uid 'RBNU_NCLOAD' -Text 'Load' -Command 'NCLOAD' -Style 'large' -Tooltip 'Download and load instruments from server'))
    [void]$panels.Append("`n")
    [void]$panels.Append("        </RibbonRow>`n")
    [void]$panels.Append("      </RibbonRowPanel>`n")
    [void]$panels.Append("    </RibbonPanelSource>`n")
    [void]$refs.Append("      <RibbonPanelSourceReference UID=`"RBNU_NCTOOLS_CTRL_Ref`" PanelId=`"RBNU_NCTOOLS_CTRL`" ResizeStyle=`"Default`" />`n")

    # Panel/button captions come from NC_TestRibbon.json (UTF-8 file), not from this .ps1 source.
    $panelIndex = 0
    foreach ($panelDef in $panelList) {
        $panelIndex++
        $panelText = [string]$panelDef.name
        if ([string]::IsNullOrWhiteSpace($panelText)) {
            $panelText = [string]$panelDef.panel
        }
        if ([string]::IsNullOrWhiteSpace($panelText)) {
            throw ("Ribbon panel[$panelIndex] missing name")
        }

        $panelUid = 'RBNU_NCTOOLS_P' + $panelIndex + '_' + (Get-NcSanitizeId $panelText)
        [void]$panels.Append("    <RibbonPanelSource UID=`"$panelUid`" Text=`"$(Get-NcXmlEscape $panelText)`" HiddenInEditor=`"false`">`n")
        [void]$panels.Append("      <RibbonRowPanel UID=`"RBNURP_$panelUid`" ResizeStyle=`"None`" ResizePriority=`"100`" TopJustify=`"True`">`n")
        [void]$panels.Append("        <RibbonRow UID=`"RBNURR_$panelUid`">`n")

        $btnIndex = 0
        foreach ($btn in @($panelDef.buttons)) {
            $btnIndex++
            $style = if ([string]$btn.style -eq 'small') { 'small' } else { 'large' }
            $text = [string]$btn.text
            $btnType = [string]$btn.type
            if ($btnType -eq 'flyout' -or $btnType -eq 'split' -or $btnType -eq 'dropdown') {
                $uid = 'RBNU_FLY_' + $panelIndex + '_' + $btnIndex + '_' + (Get-NcSanitizeId $text)
                $items = @($btn.items)
                [void]$panels.Append((Get-NcRibbonFlyoutXml -Uid $uid -Text $text -Style $style -Items $items))
                [void]$panels.Append("`n")
                continue
            }

            $cmdName = [string]$btn.command
            $uid = 'RBNU_BTN_' + $panelIndex + '_' + (Get-NcSanitizeId $cmdName)
            [void]$panels.Append((Get-NcRibbonButtonXml -Uid $uid -Text $text -Command $cmdName -Style $style -Tooltip $text))
            [void]$panels.Append("`n")
        }

        [void]$panels.Append("        </RibbonRow>`n")
        [void]$panels.Append("      </RibbonRowPanel>`n")
        [void]$panels.Append("    </RibbonPanelSource>`n")
        [void]$refs.Append("      <RibbonPanelSourceReference UID=`"${panelUid}_Ref`" PanelId=`"$panelUid`" ResizeStyle=`"Default`" />`n")
    }

    $ribbonRoot = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<RibbonRoot>'
        '  <RibbonPanelSourceCollection xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">'
        $panels.ToString().TrimEnd("`r", "`n")
        '  </RibbonPanelSourceCollection>'
        '  <RibbonTabSourceCollection>'
        '    <RibbonTabSource Text="NC Tools" UID="ID_NCTOOLS_TAB">'
        $refs.ToString().TrimEnd("`r", "`n")
        '    </RibbonTabSource>'
        '  </RibbonTabSourceCollection>'
        '</RibbonRoot>'
        ''
    ) -join "`n"

    $menuPackage = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<MenuPackageParts>'
        '  <PartData PartData_Name="/RibbonRoot.cui" />'
        '  <PartData PartData_Name="/Menu_Package_Info.xml" />'
        '</MenuPackageParts>'
        ''
    ) -join "`n"

    $contentTypes = '<?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="cui" ContentType="text/xml" /><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" /><Default Extension="xml" ContentType="text/xml" /></Types>'

    $cuixPath = Join-Path $ConfigDir 'NC_Tools.cuix'
    if (Test-Path -LiteralPath $cuixPath) {
        Remove-Item -LiteralPath $cuixPath -Force
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $zip = [System.IO.Compression.ZipFile]::Open($cuixPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in @(
                @{ Name = 'RibbonRoot.cui'; Content = $ribbonRoot },
                @{ Name = 'Menu_Package_Info.xml'; Content = $menuPackage },
                @{ Name = '[Content_Types].xml'; Content = $contentTypes }
            )) {
            $e = $zip.CreateEntry($entry.Name)
            $sw = New-Object System.IO.StreamWriter($e.Open(), $utf8NoBom)
            try { $sw.Write($entry.Content) }
            finally { $sw.Dispose() }
        }
    }
    finally {
        $zip.Dispose()
    }

    # --- nanoCAD.cfg #include (NcBootstrap.ProfileInstaller.EnsureUiInstalled) ---
    $masterCfg = Join-Path $ConfigDir 'nanoCAD.cfg'
    if (-not (Test-Path -LiteralPath $masterCfg)) {
        throw "nanoCAD.cfg not found: $masterCfg"
    }

    $includeLine = '#include "' + $cfgPath + '"'
    $masterText = [IO.File]::ReadAllText($masterCfg)
    # Drop stale NC_Tools includes (any path), then ensure current one.
    $lines = [IO.File]::ReadAllLines($masterCfg)
    $kept = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($line -match 'NC_Tools\.cfg"') { continue }
        $kept.Add($line)
    }
    $kept.Add($includeLine)
    [IO.File]::WriteAllLines($masterCfg, $kept, (New-Object System.Text.UTF8Encoding($true)))

    Write-Host ("UI ready: {0} + {1}" -f $cfgPath, $cuixPath)
    Write-Host ("include: {0}" -f $includeLine)

    # syncFolder unused for generation (icons come from package); keep param for API parity
    $null = $SyncFolder
}
