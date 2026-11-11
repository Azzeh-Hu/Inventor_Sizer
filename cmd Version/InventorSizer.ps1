<#
.SYNOPSIS
    InventorSizer - drive an Autodesk Inventor panel assembly from its file name, with hole
    rules, product standards and vector drawings of the result.

.DESCRIPTION
    File name  [project ]FP-386x656x47  <->  Parameters.xml  ->  Inventor user parameters
    (assembly + every part that has a parameter with the same name). Model dimensions
    reference those user parameters through formulas set up in Inventor.

    - Type: the product standard (standards\*.json): FP, TP or RP - name prefix, height,
      master assembly, parts. The type follows the name ("6357-3 TP-519x1194x42" -> TP).
    - Panels wider than the split width get the inner panel (-In) split in 2: the part is
      halved and a second copy is added to the assembly and mated to the first.
    - Type a new name (e.g. FP-400x700x50) or edit the values in the grid; both stay in sync.
    - HolesL / HolesW set the holes per wall; the hole rules turn them into pattern values,
      and Apply is blocked while a rule is broken (the reasons are shown in red).
    - The drawing (any view, with bill of materials and axis) updates as you type;
      "Save drawing" writes a drawing sheet as SVG.
    - "Apply" saves Parameters.xml, pushes the values into Inventor and rebuilds. If the size in
      the name changed, each new file is saved next to the file it was made from (Part Number
      included) and the originals are left untouched.

    lib\Common.ps1     logging, settings.json
    lib\ParamFile.ps1  Parameters.xml (Inventor's parameter export format)
    lib\HoleRules.ps1  hole count -> positions, pattern values and checks
    lib\Standards.ps1  product types (standards\*.json) and the bill of materials
    lib\Inventor.ps1   COM: open, push parameters, inner-panel split, rebuild, save as new size
    lib\Twin.ps1       drawings: views, bill of materials, axis, window painting, SVG sheet
    lib\Hardware.ps1   hinge / handle holes: learned from a master, re-placed for every size
    lib\GrModel.ps1    GR (unit frame): frames, profiles, panels from an export or Inventor
    lib\GrTwin.ps1     GR drawings: frame wireframes, frame table, cut list

    Needs only what ships with Windows plus Inventor (Windows PowerShell 5.1 + COM).
    Start it with Launch.bat.
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$Root = $PSScriptRoot
$Lib = Join-Path $Root 'lib'
. (Join-Path $Lib 'Common.ps1')
. (Join-Path $Lib 'ParamFile.ps1')
. (Join-Path $Lib 'HoleRules.ps1')
. (Join-Path $Lib 'Standards.ps1')
. (Join-Path $Lib 'Inventor.ps1')
. (Join-Path $Lib 'Twin.ps1')
. (Join-Path $Lib 'Hardware.ps1')
# GR code (lib\GrModel.ps1, lib\GrTwin.ps1) loads on the first switch to GR editing.

$ParamPath    = Join-Path $Root 'Parameters.xml'
$SettingsPath = Join-Path $Root 'settings.json'
$StandardsDir = Join-Path $Root 'standards'
$GrPresetDir  = Join-Path $Root 'presets\GR'
$FixedParams  = @('HoleDia')   # shown read-only: the hole diameter is always 4.1 mm
$State = @{
    Settings = (Read-Settings $SettingsPath); Standards = @(); Standard = $null
    Values = @{}; Layout = $null; Shapes = $null
    DefaultsFor = ''   # id of the type whose defaults (HoleZ, ...) were applied last
    Hardware = $null   # hole template of the selected type ("hardware" in standards\<type>.json)
    HardwareHoles = @()
    Mode = 'Panel'     # drawing mode: Panel or GR
    Gr = $null         # GR model (lib\GrModel.ps1)
}
$AutoParams = @('HolesL', 'HolesW')   # read-only while automatic hole counts are on
$script:Syncing = $false

# Loads the GR code the first time GR editing is used. The files are read at script level
# (function: and $script: prefixes), so their functions stay available after this returns.
function Import-GrModules {
    if (Get-Command Build-GrModel -ErrorAction SilentlyContinue) { return }
    Measure-Dev 'Load the GR modules' {
        foreach ($f in 'GrModel.ps1', 'GrTwin.ps1') {
            $code = [IO.File]::ReadAllText((Join-Path $Lib $f))
            $code = [regex]::Replace($code, '(?m)^function\s+(?!script:)', 'function script:')
            $code = [regex]::Replace($code, '(?m)^\$(Gr\w+)\s*=', '$$script:$1 =')
            . ([scriptblock]::Create($code))
        }
        Read-GrPartRules (Join-Path $StandardsDir 'GR.json')
    }
}

# ------------------------------------------------------------------ grid data

$table = New-Object System.Data.DataTable
[void]$table.Columns.Add('Name', [string])
[void]$table.Columns.Add('Value', [double])
[void]$table.Columns.Add('Unit', [string])
[void]$table.Columns.Add('Description', [string])

function Find-Row([string]$name) {
    foreach ($r in $table.Rows) { if ($r['Name'] -eq $name) { return $r } }
    $null
}

function Set-Grid([object[]]$params) {
    $script:Syncing = $true
    try {
        $table.Rows.Clear()
        foreach ($p in $params) { [void]$table.Rows.Add($p.Name, $p.Value, $p.Unit, $p.Description) }
        $table.AcceptChanges()
    } finally { $script:Syncing = $false }
}

function Get-GridParams {
    [void]$grid.EndEdit()
    $grid.BindingContext.get_Item($table).EndCurrentEdit()
    $out = foreach ($r in $table.Rows) {
        if ($r['Value'] -is [DBNull]) { throw "Parameter '$($r['Name'])' has no value." }
        New-Param $r['Name'] ([double]$r['Value']) $r['Unit'] $r['Description']
    }
    , @($out)
}

function Get-GridValues {
    $values = @{}
    foreach ($r in $table.Rows) { if ($r['Value'] -isnot [DBNull]) { $values[$r['Name']] = [double]$r['Value'] } }
    $values
}

function Get-SizeToken {
    $vals = @()
    foreach ($n in $SizeParams) {
        $r = Find-Row $n
        if ($null -eq $r -or $r['Value'] -is [DBNull]) { return $null }
        $vals += Format-Number $r['Value']
    }
    $vals -join 'x'
}

# Grid -> name box
function Update-NameFromGrid {
    $token = Get-SizeToken
    if (-not $token) { return }
    $script:Syncing = $true
    try {
        if ($SizeRegex.IsMatch($nameBox.Text)) { $nameBox.Text = $SizeRegex.Replace($nameBox.Text, $token, 1) }
        else { $nameBox.Text = "$($State.Standard.Prefix)$token" }
    } finally { $script:Syncing = $false }
}

# Name box -> grid
function Update-GridFromName {
    $m = $SizeRegex.Match($nameBox.Text)
    if (-not $m.Success) { return }
    $script:Syncing = $true
    try {
        for ($i = 0; $i -lt $SizeParams.Count; $i++) {
            $r = Find-Row $SizeParams[$i]
            if ($r) { $r['Value'] = [double]::Parse($m.Groups[$i + 1].Value, $Invariant) }
        }
    } finally { $script:Syncing = $false }
}

# ------------------------------------------------------------------ product types

function Select-Standard($Standard) {
    $State.Standard = $Standard
    $State.Hardware = Read-HardwareTemplate $StandardsDir $Standard
    if ($grid.Rows.Count) { Update-GridLocks }
    $script:Syncing = $true
    try { $typeBox.SelectedIndex = [array]::IndexOf($State.Standards, $Standard) } finally { $script:Syncing = $false }
    $btnMaster.Enabled = [bool]($Standard.Master -and (Test-Path $Standard.Master))
}

# Puts the type's own values (e.g. HoleZ: TP 20, FP 22) into the grid, once per type change.
# The height is only forced when the type was picked from the list; otherwise the name decides.
function Set-TypeDefaults($Standard, [bool]$WithHeight) {
    if ($State.DefaultsFor -eq $Standard.Id -and -not $WithHeight) { return }
    $State.DefaultsFor = $Standard.Id
    $wasSyncing = $script:Syncing
    $script:Syncing = $true
    try {
        foreach ($k in $Standard.Defaults.Keys) {
            $r = Find-Row $k
            if ($r -and $r['Value'] -ne $Standard.Defaults[$k]) {
                $r['Value'] = $Standard.Defaults[$k]
                Write-Log "  $($Standard.Family): $k = $(Format-Number $Standard.Defaults[$k])"
            }
        }
        if ($WithHeight -and $Standard.Height -gt 0) {
            $r = Find-Row 'Height'
            if ($r) { $r['Value'] = $Standard.Height }
        }
    } finally { $script:Syncing = $wasSyncing }
}

# Picks the type from the name: the prefix in front of the size, else the height.
function Update-StandardFromName {
    $match = Find-StandardForName $State.Standards $nameBox.Text
    if (-not $match) { return }
    if ($match -ne $State.Standard) {
        Select-Standard $match
        Write-Log "Type: $($match.Name)"
    }
    Set-TypeDefaults $match $false
}

# The user picked another type: swap the name prefix (the project number in front stays) and
# take over the type's height and defaults.
function Set-StandardFromList {
    $new = $State.Standards[$typeBox.SelectedIndex]
    Select-Standard $new
    $script:Syncing = $true
    try { $nameBox.Text = Set-PanelNamePrefix $State.Standards $nameBox.Text $new.Prefix } finally { $script:Syncing = $false }
    Set-TypeDefaults $new $true
    Update-NameFromGrid
    Update-Preview
}

# ------------------------------------------------------------------ rules + drawing

# Automatic hole counts: fills HolesL / HolesW from the size (maximum pitch in Settings).
function Update-AutoHoles {
    $pitch = [double]$State.Settings.maxHolePitch
    if ($pitch -le 0) { return }
    $v = Get-GridValues
    if (-not ($v.ContainsKey('Width') -and $v.ContainsKey('Length') -and $v.ContainsKey('HoleEdge'))) { return }
    $want = @{
        HolesL = Get-AutoHoleCount $v['Length'] $v['HoleEdge'] $pitch $false
        HolesW = Get-AutoHoleCount $v['Width']  $v['HoleEdge'] $pitch $true
    }
    $wasSyncing = $script:Syncing
    $script:Syncing = $true
    try {
        foreach ($k in $want.Keys) {
            $r = Find-Row $k
            if ($r -and ($r['Value'] -is [DBNull] -or [double]$r['Value'] -ne $want[$k])) { $r['Value'] = [double]$want[$k] }
        }
    } finally { $script:Syncing = $wasSyncing }
}

function Update-GridLocks {
    foreach ($gr in $grid.Rows) {
        $name = [string]$gr.Cells['Name'].Value
        $locked = ($FixedParams -contains $name) -or ($State.Settings.maxHolePitch -gt 0 -and $AutoParams -contains $name) -or
                  ($name -in 'HingeCount', 'HandleCount' -and -not ($State.Standard -and $State.Standard.HardwareCounts))
        $gr.ReadOnly = $locked
        $gr.DefaultCellStyle.ForeColor = $(if ($locked) { [System.Drawing.Color]::Gray } else { [System.Drawing.Color]::Black })
    }
}

# Re-runs the rules, shows any broken rule (red, locks Apply) or the notes (grey), and redraws.
function Update-Preview {
    Update-AutoHoles
    $State.Values = Get-GridValues
    $State.HardwareHoles = @()
    $errors = @()
    $counts = Get-HardwareCounts $State.Standard $State.Values
    foreach ($k in @($counts.Keys)) {
        if ($counts[$k] -lt 1 -or $counts[$k] -ne [math]::Round($counts[$k])) { $errors += "$k count must be a whole number of 1 or more."; $counts.Remove($k) }
    }
    if ($State.Hardware -and $State.Values['Width'] -gt 0 -and $State.Values['Length'] -gt 0 -and $State.Values['Height'] -gt 0) {
        $State.HardwareHoles = Get-HardwareHoles $State.Hardware $State.Values['Width'] $State.Values['Length'] $State.Values['Height'] $counts
    }
    try {
        $State.Layout = Get-HoleLayout $State.Values $State.Settings $State.Standard
        $errors = @($State.Layout.Errors)
    } catch {
        $State.Layout = $null
        $errors = @($_.Exception.Message)
    }
    if (-not $SizeRegex.IsMatch($nameBox.Text)) { $errors += 'The name must contain a size like 386x656x47.' }
    $lines = $errors
    if ($errors.Count -eq 0 -and $State.Layout) {
        $hwNotes = Get-HardwareNotes $State.HardwareHoles   # assigning unwraps the returned list
        $lines = @($State.Layout.Notes) + @($hwNotes)
    }
    $errLabel.ForeColor = $(if ($errors.Count) { [System.Drawing.Color]::DarkRed } else { [System.Drawing.Color]::DimGray })
    $errLabel.Text = ($lines -join [Environment]::NewLine)
    $errLabel.Visible = $lines.Count -gt 0
    $btnApply.Enabled = $errors.Count -eq 0
    Update-Drawing
}

function Get-CurrentBom {
    if (-not $State.Standard) { return $null }
    Get-BomInfo $State.Standard $State.Values $State.Settings.thickness $nameBox.Text.Trim() $State.Layout
}

function Update-Drawing {
    $size = $twinBox.ClientSize
    if ($State.Mode -eq 'GR') {
        $State.Shapes = Get-GrWindowShapes $State.Gr ([string]$viewBox.SelectedItem) $size.Width $size.Height
        $twinBox.Invalidate()
        return
    }
    $State.Shapes = Get-WindowShapes $State.Values $State.Layout ([string]$viewBox.SelectedItem) $size.Width $size.Height `
                        $chkHidden.Checked (Get-CurrentBom) $State.Settings.thickness $State.HardwareHoles
    $twinBox.Invalidate()
}

function Update-Status {
    $info = Get-InventorInfo
    $statusLabel.Text = $(if (-not $info.connected) { 'Inventor: not running' }
                          elseif ($info.isAssembly) { "Inventor: $($info.document)" }
                          elseif ($info.document) { 'Inventor: active document is not an assembly' }
                          else { 'Inventor: no document open' })
}

# ------------------------------------------------------------------ settings

function Update-SettingsFromForm {
    if ($script:Syncing) { return }
    $State.Settings.minHoleGap = [double]$gapBox.Value
    $State.Settings.thickness  = [double]$thickBox.Value
    $State.Settings.splitWidth = [double]$splitBox.Value
    $State.Settings.maxHolePitch = [double]$pitchBox.Value
}

function Save-FormSettings {
    Update-SettingsFromForm
    $State.Settings = Save-Settings $SettingsPath ([pscustomobject]$State.Settings)
}

function Update-FolderControls {
    $folderBox.Enabled = $chkFolder.Checked
    $btnBrowse.Enabled = $chkFolder.Checked
    $folderInfo.Text = $(if ($chkFolder.Checked) { 'This session only: new files for new sizes go into the folder above.' }
                         else { 'New panels are made from the type''s master; you choose their folder on Apply.' })
}

# Settings for Apply: the saved ones plus the folder, only when chosen for this session.
function Get-ApplySettings {
    $s = [ordered]@{}
    foreach ($k in $State.Settings.Keys) { $s[$k] = $State.Settings[$k] }
    $s.outputFolder = ''
    if ($chkFolder.Checked) {
        if (-not $folderBox.Text.Trim()) { throw 'Choose a folder for new sizes, or untick "Save new sizes to another folder".' }
        $s.outputFolder = $folderBox.Text.Trim()
    }
    $s
}

function Select-OutputFolder {
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = 'Folder for new panel sizes (this session only)'
    if ($folderBox.Text -and (Test-Path $folderBox.Text)) { $dlg.SelectedPath = $folderBox.Text }
    if ($dlg.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) { $folderBox.Text = $dlg.SelectedPath }
}

# ------------------------------------------------------------------ actions

function Invoke-Step([string]$Title, [scriptblock]$Body) {
    $form.Cursor = [System.Windows.Forms.Cursors]::WaitCursor
    $form.Enabled = $false
    try {
        Write-Log "== $Title"
        Measure-Dev $Title $Body
        Write-Log 'Done.'
    } catch {
        Write-Log "ERROR: $(Format-Error $_)"
        [void][System.Windows.Forms.MessageBox]::Show($form, $_.Exception.Message, $Title,
            [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
    } finally {
        $form.Enabled = $true
        $form.Cursor = [System.Windows.Forms.Cursors]::Default
        if ($Title -match 'Inventor|Open|Apply|Write|Learn|Set up') { Update-Status }   # only steps that use Inventor ask it
    }
}

# Panel assemblies fill the name and grid; a GR assembly (name ending in GR, no panel size)
# switches the drawing to the GR view.
function Read-FromInventor {
    $asm = Get-ActiveAssembly
    $name = [IO.Path]::GetFileNameWithoutExtension($asm.FullFileName)
    if (-not $SizeRegex.IsMatch($name) -and $name -match '(?:^|[\s-])GR$') {
        Write-Log "Active assembly: $($asm.FullFileName) (GR)"
        Import-GrModules
        Set-GrModel (Build-GrModel (Read-GrFromInventor))
        return
    }
    $nameBox.Text = Read-ActiveAssemblyName   # fills the grid through TextChanged
    if ($State.Mode -eq 'GR') { Set-TwinMode 'Panel' }
}

# Panel editing (FP / RP / TP) or GR editing: each mode shows only its own controls and runs
# only its own code; the GR code is loaded on the first switch.
function Set-TwinMode([string]$Mode) {
    if ($Mode -eq 'GR') { Import-GrModules }
    $State.Mode = $Mode
    $gr = $Mode -eq 'GR'
    $btnMode.Text = $(if ($gr) { 'Switch to panel editing' } else { 'Switch to GR editing' })
    $modeLabel.Text = $(if ($gr) { 'Mode: GR editing (unit frame)' } else { 'Mode: panel editing (FP / RP / TP)' })
    $form.Text = "Inventor Sizer - $(if ($gr) { 'GR editing' } else { 'panel editing' })"
    foreach ($c in @($typeBox, $btnMaster, $btnSetMaster, $nameBox, $btnApply, $btnReload, $btnLearn, $btnSetup, $chkHidden)) { $c.Visible = -not $gr }
    foreach ($c in @($btnGrLoad, $btnGrInv, $btnOpen, $grPresetBox, $grPresetLabel)) { $c.Visible = $gr }
    if ($gr) { Update-GrPresets }
    $chkHidden.Enabled = -not $gr
    # Left side: the panel grid and settings, or the GR frame editor.
    $grid.Visible = -not $gr; $settingsBox.Visible = -not $gr; $grPanel.Visible = $gr
    if ($gr) { $errLabel.Visible = $false; $btnApply.Enabled = $false; Update-GrGrid; Update-Drawing } else { Update-Preview }
}

# ------------------------------------------------------------------ GR editor

function Update-GrGrid {
    $script:Syncing = $true
    try {
        $grTable.Rows.Clear()
        if ($State.Gr) {
            foreach ($f in ($State.Gr.Frames | Sort-Object No)) {
                [void]$grTable.Rows.Add((Get-GrBoxLabel $f), ($f.Lx + $GrCornerAdd), ($f.Wy + $GrCornerAdd), ($f.Hz + $GrCornerAdd),
                    "$(Format-Number $f.Lx) / $(Format-Number $f.Wy) / $(Format-Number $f.Hz)", $f.Panels.Count, $f.Dividers.Count)
            }
        }
        $grTable.AcceptChanges()
        $grDivTable.Rows.Clear()
        if ($State.Gr) {
            $frameNo = @{}; foreach ($f in $State.Gr.Frames) { $frameNo[$f.Id] = Get-GrBoxLabel $f }
            foreach ($c in ($State.Gr.Components.Values | Where-Object Kind -eq 'divider' | Sort-Object { $frameNo[$_.Frame] }, Name)) {
                $pos = $(if ($null -ne $c.Position) { [double]$c.Position } else { [DBNull]::Value })
                [void]$grDivTable.Rows.Add($c.Name, $frameNo[$c.Frame], $c.Length, $pos, $(if ($c.PosConstraint) { "offset ($($c.PosConstraint))" } else { 'joints' }))
            }
        }
        $grDivTable.AcceptChanges()
    } finally { $script:Syncing = $false }
    $grInfo.Text = $(if ($State.Gr) {
        "$($State.Gr.Name)$(if ($State.Gr.Edited) { ' (edited)' }): outer $(Format-Number $State.Gr.Length) x $(Format-Number $State.Gr.OuterWidth) x $(Format-Number $State.Gr.OuterHeight) mm. " +
        'Edit the outer sizes (incl. corners); the P150 profiles are outer - 260. Select a box to add a box next to it.'
    } else { 'Load a GR export or read a GR from Inventor.' })
}

function Get-SelectedGrFrame {
    if (-not $State.Gr) { throw 'Load a GR first.' }
    $row = $grGrid.CurrentRow
    if ($null -eq $row) { throw 'Select a frame in the list first.' }
    Get-GrNoFromLabel ([string]$row.Cells['Box'].Value)
}

function Invoke-GrEdit([string]$Title, [scriptblock]$Body) {
    try { & $Body } catch { Write-Log "ERROR ($Title): $($_.Exception.Message)" }
    Update-GrGrid
    Update-Drawing
}

function Update-GrPresets {
    $script:Syncing = $true
    try {
        $grPresetBox.Items.Clear()
        if (Test-Path $GrPresetDir) { foreach ($f in (Get-ChildItem $GrPresetDir -Filter '*.json' | Sort-Object Name)) { [void]$grPresetBox.Items.Add($f.BaseName) } }
    } finally { $script:Syncing = $false }
}

function Save-GrDesignFile {
    if (-not $State.Gr) { throw 'Load a GR first.' }
    if (-not (Test-Path $GrPresetDir)) { [void](New-Item -ItemType Directory -Path $GrPresetDir) }
    $dlg = New-Object System.Windows.Forms.SaveFileDialog
    $dlg.InitialDirectory = $GrPresetDir   # saved here, a design is a preset in the list
    $dlg.Filter = 'GR design (*.json)|*.json'
    $dlg.FileName = "$($State.Gr.Name)-design.json"
    if ($dlg.ShowDialog($form) -ne [System.Windows.Forms.DialogResult]::OK) { return }
    Save-GrDesign $State.Gr $dlg.FileName
    Write-Log "Saved GR design $($dlg.FileName)"
    Update-GrPresets
}

function Open-GrDesignFile {
    $dlg = New-Object System.Windows.Forms.OpenFileDialog
    $dlg.Filter = 'GR design (*.json)|*.json'
    if ($dlg.ShowDialog($form) -ne [System.Windows.Forms.DialogResult]::OK) { return }
    Import-GrModules
    Set-GrModel (Read-GrDesign $dlg.FileName)
}

function Save-GrCutListFile {
    if (-not $State.Gr) { throw 'Load a GR first.' }
    $dlg = New-Object System.Windows.Forms.SaveFileDialog
    $dlg.Filter = 'Cut list (*.csv)|*.csv'
    $dlg.FileName = "$($State.Gr.Name)-cut-list.csv"
    if ($dlg.ShowDialog($form) -ne [System.Windows.Forms.DialogResult]::OK) { return }
    Export-GrCutList $State.Gr $dlg.FileName
    Write-Log "Saved cut list $($dlg.FileName)"
}

function Set-GrModel($Model) {
    $State.Gr = $Model
    Write-Log ("GR {0}: {1} frame(s), {2} panels, outer about {3} x {4} x {5} mm ({6})" -f $Model.Name, $Model.Frames.Count,
        @($Model.Components.Values | Where-Object { $_.Kind -eq 'panel' }).Count, (Format-Number $Model.Length), (Format-Number $Model.OuterWidth),
        (Format-Number $Model.OuterHeight), $Model.Source)
    foreach ($w in $Model.Warnings) { Write-Log "  note: $w" }
    Set-TwinMode 'GR'
    Update-GrGrid
}

function Open-GrExport {
    $dlg = New-Object System.Windows.Forms.OpenFileDialog
    $dlg.Filter = 'GR export (*.xml)|*.xml'
    $dlg.Title = 'GR export: select the tree and the constraints file (or one export that has both)'
    $dlg.Multiselect = $true
    if ($dlg.ShowDialog($form) -ne [System.Windows.Forms.DialogResult]::OK) { return }
    Import-GrModules
    Set-GrModel (Build-GrModel (Read-GrExportFiles $dlg.FileNames))
}

function Open-AssemblyFile {
    $dlg = New-Object System.Windows.Forms.OpenFileDialog
    $dlg.Filter = 'Inventor assembly (*.iam)|*.iam'
    $dlg.Title = 'Open assembly'
    if ($State.Standard.Master -and (Test-Path $State.Standard.Master)) { $dlg.InitialDirectory = Split-Path $State.Standard.Master }
    if ($dlg.ShowDialog($form) -ne [System.Windows.Forms.DialogResult]::OK) { return }
    [void](Open-InventorAssembly $dlg.FileName)
    Read-FromInventor
}

function Invoke-ApplyToInventor {
    $settings = Get-ApplySettings
    if (-not $settings.outputFolder) {   # new panels always go to a chosen folder (the master folder stays clean)
        $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
        $dlg.Description = 'Folder for the new panel files (kept for this session)'
        if ($dlg.ShowDialog($form) -ne [System.Windows.Forms.DialogResult]::OK) { return }
        $chkFolder.Checked = $true; $folderBox.Text = $dlg.SelectedPath; $settings.outputFolder = $dlg.SelectedPath
    }
    Save-FormSettings
    $newName = Invoke-Apply $nameBox.Text (Get-GridParams) $settings $ParamPath $State.Standards $State.Standard
    $script:Syncing = $true
    try { $nameBox.Text = $newName } finally { $script:Syncing = $false }
}

# Reads the hinge / handle holes of the active master and keeps them for the selected type.
function Invoke-LearnHardwareStep {
    $v = Get-GridValues
    [void](Invoke-LearnHardware $State.Standards $State.Standard $StandardsDir ([double]$v['HoleDia']) ([double]$v['HoleZ']))
    $State.Hardware = Read-HardwareTemplate $StandardsDir $State.Standard
    Update-Preview
}

function Invoke-SetupStep {
    $std = $State.Standard
    $msg = ("Set up the open master as type $($std.Name)?`n`nThis creates the user parameters and writes the formulas from $($std.File) " +
            "into the master's parts. Do it on a copy first.")
    $ok = [System.Windows.Forms.MessageBox]::Show($form, $msg, 'Set up master', [System.Windows.Forms.MessageBoxButtons]::OKCancel, [System.Windows.Forms.MessageBoxIcon]::Question)
    if ($ok -ne [System.Windows.Forms.DialogResult]::OK) { return }
    Invoke-MasterSetup $std
}

function Save-Drawing {
    $dlg = New-Object System.Windows.Forms.SaveFileDialog
    $dlg.Filter = 'SVG drawing (*.svg)|*.svg'
    $dlg.FileName = "$($nameBox.Text.Trim()).svg"
    if ($dlg.ShowDialog($form) -ne [System.Windows.Forms.DialogResult]::OK) { return }
    if ($State.Mode -eq 'GR') {
        Export-TwinSvg $dlg.FileName (Get-GrSheetShapes $State.Gr) 1400 990 $(if ($State.Gr) { $State.Gr.Name } else { 'GR' })
        Write-Log "Saved drawing $($dlg.FileName)"
        return
    }
    $shapes = Get-SheetShapes $State.Values $State.Layout (Get-CurrentBom) $State.Settings.thickness $chkHidden.Checked $State.HardwareHoles
    Export-TwinSvg $dlg.FileName $shapes 1400 990 $nameBox.Text.Trim()
    Write-Log "Saved drawing $($dlg.FileName)"
}

# ------------------------------------------------------------------ form

function New-Control([string]$Type, [hashtable]$Props) {
    $c = New-Object "System.Windows.Forms.$Type"
    foreach ($k in $Props.Keys) { $c.$k = $Props[$k] }
    $c
}
function New-Point([int]$X, [int]$Y) { New-Object System.Drawing.Point($X, $Y) }
function New-Size([int]$W, [int]$H) { New-Object System.Drawing.Size($W, $H) }

$form = New-Control 'Form' @{ Text = 'Inventor Sizer'; Size = (New-Size 1240 820); StartPosition = 'CenterScreen'
                              Font = (New-Object System.Drawing.Font('Segoe UI', 9)) }

# Left: parameter grid, broken rules, settings. Right: drawing.
$split = New-Control 'SplitContainer' @{ Dock = 'Fill'; Orientation = 'Vertical' }

$grid = New-Control 'DataGridView' @{ Dock = 'Fill'; AllowUserToAddRows = $false; AllowUserToDeleteRows = $false
                                      AutoSizeColumnsMode = 'Fill'; RowHeadersVisible = $false; SelectionMode = 'CellSelect' }
$grid.DataSource = $table

$errLabel = New-Control 'Label' @{ Dock = 'Bottom'; AutoSize = $false; Height = 64; Visible = $false
                                   ForeColor = [System.Drawing.Color]::DarkRed; Padding = (New-Object System.Windows.Forms.Padding(4)) }

# Settings: a table layout keeps every control inside the box at any width.
$settingsBox = New-Control 'GroupBox' @{ Dock = 'Bottom'; Height = 228; Text = 'Settings' }
$settingsGrid = New-Control 'TableLayoutPanel' @{ Dock = 'Fill'; ColumnCount = 3; RowCount = 7 }
[void]$settingsGrid.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::AutoSize)))
[void]$settingsGrid.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 100)))
[void]$settingsGrid.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::AutoSize)))
$chkFolder  = New-Control 'CheckBox' @{ Text = 'Folder for new panels (this session only)'; AutoSize = $true; Anchor = 'Left' }
$folderBox  = New-Control 'TextBox' @{ Anchor = 'Left,Right' }
$btnBrowse  = New-Control 'Button' @{ Text = 'Browse...'; AutoSize = $true; Anchor = 'Right' }
$gapBox     = New-Control 'NumericUpDown' @{ Width = 80; DecimalPlaces = 1; Maximum = 1000; Increment = 0.5; Anchor = 'Left' }
$thickBox   = New-Control 'NumericUpDown' @{ Width = 80; DecimalPlaces = 2; Maximum = 50; Increment = 0.1; Anchor = 'Left' }
$thickBox.Minimum = 0.1
$pitchBox   = New-Control 'NumericUpDown' @{ Width = 80; DecimalPlaces = 0; Maximum = 100000; Increment = 10; Anchor = 'Left' }
$splitBox   = New-Control 'NumericUpDown' @{ Width = 80; DecimalPlaces = 0; Maximum = 100000; Increment = 10; Anchor = 'Left' }
$folderInfo = New-Control 'Label' @{ AutoSize = $true; Anchor = 'Left'; ForeColor = [System.Drawing.Color]::DimGray }
$settingsGrid.Controls.Add($chkFolder, 0, 0); $settingsGrid.SetColumnSpan($chkFolder, 3)
$settingsGrid.Controls.Add((New-Control 'Label' @{ Text = 'Folder:'; AutoSize = $true; Anchor = 'Left' }), 0, 1)
$settingsGrid.Controls.Add($folderBox, 1, 1)
$settingsGrid.Controls.Add($btnBrowse, 2, 1)
$settingsGrid.Controls.Add($folderInfo, 0, 2); $settingsGrid.SetColumnSpan($folderInfo, 3)
$settingsGrid.Controls.Add((New-Control 'Label' @{ Text = 'Minimum hole gap (mm):'; AutoSize = $true; Anchor = 'Left' }), 0, 3)
$settingsGrid.Controls.Add($gapBox, 1, 3)
$settingsGrid.Controls.Add((New-Control 'Label' @{ Text = 'Sheet thickness, one sheet (Staerke, mm):'; AutoSize = $true; Anchor = 'Left' }), 0, 4)
$settingsGrid.Controls.Add($thickBox, 1, 4)
$settingsGrid.Controls.Add((New-Control 'Label' @{ Text = 'Split inner panel above width (mm, 0 = never):'; AutoSize = $true; Anchor = 'Left' }), 0, 5)
$settingsGrid.Controls.Add($splitBox, 1, 5)
$settingsGrid.Controls.Add((New-Control 'Label' @{ Text = 'Automatic hole counts, max. pitch (mm, 0 = by hand):'; AutoSize = $true; Anchor = 'Left' }), 0, 6)
$settingsGrid.Controls.Add($pitchBox, 1, 6)
$settingsBox.Controls.Add($settingsGrid)

# GR editor (shown instead of the panel grid in GR mode): one row per frame.
$grTable = New-Object System.Data.DataTable
[void]$grTable.Columns.Add('Box', [string])
foreach ($c in 'Outer L', 'Outer W', 'Outer H') { [void]$grTable.Columns.Add($c, [double]) }   # outside size incl. corners
[void]$grTable.Columns.Add('P150 L / W / H', [string])
foreach ($c in 'Panels', 'Dividers') { [void]$grTable.Columns.Add($c, [int]) }
$grPanel = New-Control 'Panel' @{ Dock = 'Fill'; Visible = $false }
$grGrid = New-Control 'DataGridView' @{ Dock = 'Fill'; AllowUserToAddRows = $false; AllowUserToDeleteRows = $false
                                        AutoSizeColumnsMode = 'Fill'; RowHeadersVisible = $false; SelectionMode = 'CellSelect' }
$grGrid.DataSource = $grTable
$grInfo = New-Control 'Label' @{ Dock = 'Top'; Height = 48; ForeColor = [System.Drawing.Color]::DimGray; Padding = (New-Object System.Windows.Forms.Padding(4)) }
# Dividers (P350): the ones placed by an offset constraint can be moved.
$grDivTable = New-Object System.Data.DataTable
foreach ($c in 'Divider', 'Box') { [void]$grDivTable.Columns.Add($c, [string]) }
foreach ($c in 'Length', 'Position') { [void]$grDivTable.Columns.Add($c, [double]) }
[void]$grDivTable.Columns.Add('Placed by', [string])
$grDivLabel = New-Control 'Label' @{ Dock = 'Bottom'; Height = 20; Text = 'Dividers (P350) - Position = offset from the frame profile, editable where the divider has its own offset'; ForeColor = [System.Drawing.Color]::DimGray }
$grDivGrid = New-Control 'DataGridView' @{ Dock = 'Bottom'; Height = 190; AllowUserToAddRows = $false; AllowUserToDeleteRows = $false
                                           AutoSizeColumnsMode = 'Fill'; RowHeadersVisible = $false; SelectionMode = 'CellSelect' }
$grDivGrid.DataSource = $grDivTable
$grButtons = New-Control 'FlowLayoutPanel' @{ Dock = 'Bottom'; Height = 200; Padding = (New-Object System.Windows.Forms.Padding(2)) }
$grButtons.Controls.AddRange(@(
    ($chkGrShared = New-Control 'CheckBox' @{ Text = 'Frames with the same width / height change together'; AutoSize = $true; Checked = $true }),
    ($btnGrAddR = New-Control 'Button' @{ Text = 'Add box right'; Size = (New-Size 105 28) }),
    ($btnGrAddL = New-Control 'Button' @{ Text = 'Add box left'; Size = (New-Size 100 28) }),
    ($btnGrAddF = New-Control 'Button' @{ Text = 'Add box front'; Size = (New-Size 105 28) }),
    ($btnGrRemove = New-Control 'Button' @{ Text = 'Remove box'; Size = (New-Size 100 28) }),
    ($btnGrSave = New-Control 'Button' @{ Text = 'Save GR design...'; Size = (New-Size 130 28) }),
    ($btnGrOpen = New-Control 'Button' @{ Text = 'Load GR design...'; Size = (New-Size 130 28) }),
    ($btnGrCut = New-Control 'Button' @{ Text = 'Save cut list (CSV)...'; Size = (New-Size 150 28) }),
    (New-Control 'Label' @{ Text = 'New GR name:'; AutoSize = $true; Margin = (New-Object System.Windows.Forms.Padding(3, 9, 0, 0)) }),
    ($grNewName = New-Control 'TextBox' @{ Width = 200 }),
    (New-Control 'Label' @{ Text = 'Part prefix:'; AutoSize = $true; Margin = (New-Object System.Windows.Forms.Padding(3, 9, 0, 0)) }),
    ($grPrefix = New-Control 'TextBox' @{ Width = 70 }),
    ($btnGrApply = New-Control 'Button' @{ Text = 'Write new GR to Inventor'; Size = (New-Size 180 28)
                                           Font = (New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)) })
))
$grPanel.Controls.Add($grGrid)
$grPanel.Controls.Add($grDivLabel)
$grPanel.Controls.Add($grDivGrid)
$grPanel.Controls.Add($grInfo)
$grPanel.Controls.Add($grButtons)
$split.Panel1.Controls.Add($grPanel)
$split.Panel1.Controls.Add($grid)
$split.Panel1.Controls.Add($errLabel)
$split.Panel1.Controls.Add($settingsBox)

$twinBox = New-Control 'PictureBox' @{ Dock = 'Fill'; BackColor = [System.Drawing.Color]::White; BorderStyle = 'FixedSingle' }
$twinBar = New-Control 'Panel' @{ Dock = 'Top'; Height = 66 }

$viewBox = New-Control 'ComboBox' @{ DropDownStyle = 'DropDownList'; Location = (New-Point 44 5); Width = 110 }
$viewBox.Items.AddRange([object[]]$TwinViews)
$twinBar.Controls.AddRange(@(
    (New-Control 'Label' @{ Text = 'View:'; AutoSize = $true; Location = (New-Point 4 9) }),
    $viewBox,
    ($chkHidden = New-Control 'CheckBox' @{ Text = 'Show hidden edges'; AutoSize = $true; Location = (New-Point 170 8) }),
    ($btnSvg = New-Control 'Button' @{ Text = 'Save drawing (SVG)...'; Location = (New-Point 310 4); Size = (New-Size 150 26) }),
    ($btnGrLoad = New-Control 'Button' @{ Text = 'Load GR export...'; Location = (New-Point 4 36); Size = (New-Size 130 26); Visible = $false }),
    ($btnGrInv = New-Control 'Button' @{ Text = 'Read GR from Inventor'; Location = (New-Point 140 36); Size = (New-Size 150 26); Visible = $false }),
    ($grPresetLabel = New-Control 'Label' @{ Text = 'Preset:'; AutoSize = $true; Location = (New-Point 300 41); Visible = $false }),
    ($grPresetBox = New-Control 'ComboBox' @{ DropDownStyle = 'DropDownList'; Location = (New-Point 350 37); Width = 230; Visible = $false })
))
$split.Panel2.Controls.Add($twinBox)
$split.Panel2.Controls.Add($twinBar)
$form.Controls.Add($split)

# Top: type + master on the first row, assembly name + open/read on the second.
$top = New-Control 'Panel' @{ Dock = 'Top'; Height = 78 }
$typeBox = New-Control 'ComboBox' @{ DropDownStyle = 'DropDownList'; Location = (New-Point 110 9); Width = 330 }
$top.Controls.AddRange(@(
    (New-Control 'Label' @{ Text = 'Type:'; AutoSize = $true; Location = (New-Point 10 12) }),
    $typeBox,
    ($btnMaster = New-Control 'Button' @{ Text = 'Open master'; Location = (New-Point 450 7); Size = (New-Size 120 27) }),
    ($statusLabel = New-Control 'Label' @{ AutoSize = $true; Location = (New-Point 930 12); ForeColor = [System.Drawing.Color]::DimGray }),
    (New-Control 'Label' @{ Text = 'Assembly name:'; AutoSize = $true; Location = (New-Point 10 47) }),
    ($nameBox = New-Control 'TextBox' @{ Location = (New-Point 110 43); Width = 330
                                         Font = (New-Object System.Drawing.Font('Segoe UI', 11)) }),
    ($btnOpen = New-Control 'Button' @{ Text = 'Open GR...'; Location = (New-Point 450 42); Size = (New-Size 120 28); Visible = $false }),
    ($btnRead = New-Control 'Button' @{ Text = 'Read from Inventor'; Location = (New-Point 576 42); Size = (New-Size 140 28); Visible = $false }),
    ($btnSetMaster = New-Control 'Button' @{ Text = 'Set master...'; Location = (New-Point 576 7); Size = (New-Size 140 27) }),
    ($btnMode = New-Control 'Button' @{ Text = 'Switch to GR editing'; Location = (New-Point 730 7); Size = (New-Size 190 28)
                                        Font = (New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)) }),
    ($modeLabel = New-Control 'Label' @{ Text = 'Mode: panel editing (FP / RP / TP)'; AutoSize = $true; Location = (New-Point 730 48); ForeColor = [System.Drawing.Color]::DarkBlue })
))
$form.Controls.Add($top)

$bottom = New-Control 'Panel' @{ Dock = 'Bottom'; Height = 170 }
$buttons = New-Control 'FlowLayoutPanel' @{ Dock = 'Top'; Height = 40; Padding = (New-Object System.Windows.Forms.Padding(6, 6, 6, 0)) }
$buttons.Controls.AddRange(@(
    ($btnApply = New-Control 'Button' @{ Text = 'Apply to Inventor'; Size = (New-Size 160 30)
                                         Font = (New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)) }),
    ($btnReload = New-Control 'Button' @{ Text = 'Reload file'; Size = (New-Size 110 30) }),
    ($btnLearn = New-Control 'Button' @{ Text = 'Learn hardware holes'; Size = (New-Size 160 30) }),
    ($btnSetup = New-Control 'Button' @{ Text = 'Set up master...'; Size = (New-Size 130 30) }),
    ($btnDevSave = New-Control 'Button' @{ Text = 'Save dev log...'; Size = (New-Size 120 30) })
))
$LogBox = New-Control 'TextBox' @{ Multiline = $true; ReadOnly = $true; ScrollBars = 'Vertical'; Dock = 'Fill'
                                   Font = (New-Object System.Drawing.Font('Consolas', 9)) }
# Dev log: every step with its time, so slow or failing steps can be found (see lib\Common.ps1).
$DevLogBox = New-Control 'TextBox' @{ Multiline = $true; ReadOnly = $true; ScrollBars = 'Both'; WordWrap = $false; Dock = 'Fill'
                                      Font = (New-Object System.Drawing.Font('Consolas', 8)) }
$DevLogBox.AppendText((($DevLines.ToArray()) -join [Environment]::NewLine) + [Environment]::NewLine)
$logTabs = New-Control 'TabControl' @{ Dock = 'Fill' }
$tabLog = New-Object System.Windows.Forms.TabPage 'Log'
$tabDev = New-Object System.Windows.Forms.TabPage 'Dev log'
$tabLog.Controls.Add($LogBox); $tabDev.Controls.Add($DevLogBox)
[void]$logTabs.TabPages.Add($tabLog); [void]$logTabs.TabPages.Add($tabDev)
$bottom.Controls.Add($logTabs)
$bottom.Controls.Add($buttons)
$form.Controls.Add($bottom)

# ------------------------------------------------------------------ events

$grid.add_DataBindingComplete({
    foreach ($c in 'Name', 'Unit', 'Description') { if ($grid.Columns[$c]) { $grid.Columns[$c].ReadOnly = $true } }
    Update-GridLocks
})

$grid.add_DataError({ param($s, $e)
    Write-Log "Invalid value in row $($e.RowIndex + 1) - enter a number."
    $e.Cancel = $true
})

$table.add_ColumnChanged({ param($s, $e)
    if ($script:Syncing -or $e.Column.ColumnName -ne 'Value') { return }
    if ($SizeParams -contains $e.Row['Name']) { Update-NameFromGrid }
    Update-Preview
})

$nameBox.add_TextChanged({
    if ($script:Syncing) { return }
    Update-StandardFromName
    Update-GridFromName
    Update-Preview
})

$typeBox.add_SelectedIndexChanged({ if (-not $script:Syncing -and $typeBox.SelectedIndex -ge 0) { Set-StandardFromList } })
$viewBox.add_SelectedIndexChanged({ Update-Drawing })
$btnMode.add_Click({ Invoke-Step 'Switch mode' { Set-TwinMode $(if ($State.Mode -eq 'GR') { 'Panel' } else { 'GR' }) } })
$btnSetMaster.add_Click({ Invoke-Step 'Set master' {
    $dlg = New-Object System.Windows.Forms.OpenFileDialog
    $dlg.Filter = 'Inventor assembly (*.iam)|*.iam'; $dlg.Title = "Master assembly for $($State.Standard.Name)"
    if ($dlg.ShowDialog($form) -ne [System.Windows.Forms.DialogResult]::OK) { return }
    Set-StandardMaster $State.Standard $dlg.FileName $Root
    Select-Standard $State.Standard
} })
# GR presets: designs in presets\GR\ next to the app; choosing one loads it without Inventor.
$grPresetBox.add_SelectedIndexChanged({
    if ($script:Syncing -or $grPresetBox.SelectedIndex -lt 0) { return }
    $file = Join-Path $GrPresetDir ([string]$grPresetBox.SelectedItem + '.json')
    Invoke-Step 'Load GR preset' { Import-GrModules; Set-GrModel (Read-GrDesign $file) }
})
$btnGrLoad.add_Click({ Invoke-Step 'Load GR export' { Open-GrExport } })
$grGrid.add_DataBindingComplete({
    foreach ($c in 'Box', 'P150 L / W / H', 'Panels', 'Dividers') { if ($grGrid.Columns[$c]) { $grGrid.Columns[$c].ReadOnly = $true } }
    foreach ($c in 'Outer L', 'Outer W', 'Outer H') { if ($grGrid.Columns[$c]) { $grGrid.Columns[$c].HeaderText = "$c (mm)" } }
})
$grGrid.add_DataError({ param($s, $e) Write-Log 'Enter a number in mm.'; $e.Cancel = $true })
$grTable.add_ColumnChanged({ param($s, $e)
    if ($script:Syncing) { return }
    $dim = @{ 'Outer L' = 'L'; 'Outer W' = 'W'; 'Outer H' = 'H' }[$e.Column.ColumnName]
    if (-not $dim -or $e.Row[$e.Column.ColumnName] -is [DBNull]) { return }
    # The outer size includes the 2 corners; the P150 profile is that minus 2 x 130.
    $script:GrPending = @{ No = (Get-GrNoFromLabel ([string]$e.Row['Box'])); Dim = $dim; Value = [double]$e.Row[$e.Column.ColumnName] - $GrCornerAdd }
    # Re-filling the grid inside its own change event upsets the grid, so the edit runs right after.
    [void]$form.BeginInvoke([Action]{
        $p = $script:GrPending; $script:GrPending = $null
        if (-not $p) { return }
        Invoke-GrEdit 'Edit frame' {
            $changed = Set-GrFrameSize $State.Gr $p.No $p.Dim $p.Value -Shared:$chkGrShared.Checked
            if ($changed) { Write-Log "GR: outer $($p.Dim) = $(Format-Number ($p.Value + $GrCornerAdd)) mm (P150 $(Format-Number $p.Value)) on $($changed -join ', ')" }
        }
    })
})
foreach ($pair in @(@($btnGrAddR, 'Right'), @($btnGrAddL, 'Left'), @($btnGrAddF, 'Front'))) {
    $pair[0].Tag = $pair[1]
    $pair[0].add_Click({ param($s, $e)
        $side = [string]$s.Tag
        Invoke-GrEdit 'Add box' {
            $ref = Get-SelectedGrFrame
            $f = Add-GrFrame $State.Gr $ref $side
            Write-Log ("GR: added {0} ({1} of Box {2}), outer {3} x {4} x {5} mm" -f (Get-GrBoxLabel $f), $side.ToLower(), ($ref - 1),
                (Format-Number ($f.Lx + $GrCornerAdd)), (Format-Number ($f.Wy + $GrCornerAdd)), (Format-Number ($f.Hz + $GrCornerAdd)))
        }
    })
}
$btnGrRemove.add_Click({ Invoke-GrEdit 'Remove box' { $no = Get-SelectedGrFrame; Remove-GrFrame $State.Gr $no; Write-Log "GR: removed Box $($no - 1) with its panels and dividers" } })
$btnGrSave.add_Click({ Invoke-Step 'Save GR design' { Save-GrDesignFile } })
$btnGrOpen.add_Click({ Invoke-Step 'Load GR design' { Open-GrDesignFile } })
$btnGrCut.add_Click({ Invoke-Step 'Save cut list' { Save-GrCutListFile } })
$btnGrApply.add_Click({ Invoke-Step 'Write new GR to Inventor' { if (-not $State.Gr) { throw 'Load a GR first.' }; Invoke-GrApply $State.Gr $grNewName.Text $grPrefix.Text; Update-GrGrid } })
$grDivGrid.add_DataBindingComplete({
    foreach ($c in 'Divider', 'Box', 'Length', 'Placed by') { if ($grDivGrid.Columns[$c]) { $grDivGrid.Columns[$c].ReadOnly = $true } }
    foreach ($r in $grDivGrid.Rows) { if (-not ([string]$r.Cells['Placed by'].Value).StartsWith('offset')) { $r.Cells['Position'].ReadOnly = $true; $r.Cells['Position'].Style.BackColor = [System.Drawing.Color]::WhiteSmoke } }
})
$grDivGrid.add_DataError({ param($s, $e) Write-Log 'Enter a number in mm.'; $e.Cancel = $true })
$grDivTable.add_ColumnChanged({ param($s, $e)
    if ($script:Syncing -or $e.Column.ColumnName -ne 'Position' -or $e.Row['Position'] -is [DBNull]) { return }
    $script:GrDivPending = @{ Name = [string]$e.Row['Divider']; Value = [double]$e.Row['Position'] }
    [void]$form.BeginInvoke([Action]{
        $p = $script:GrDivPending; $script:GrDivPending = $null
        if (-not $p) { return }
        Invoke-GrEdit 'Move divider' { Set-GrDividerPosition $State.Gr $p.Name $p.Value; Write-Log "GR: $($p.Name) at $(Format-Number $p.Value) mm" }
    })
})
$btnGrInv.add_Click({ Invoke-Step 'Read GR from Inventor' { Import-GrModules; Set-GrModel (Build-GrModel (Read-GrFromInventor)) } })
$twinBox.add_Paint({ param($s, $e) Invoke-TwinPaint $e.Graphics $State.Shapes })
# Redraw once resizing pauses (150 ms), not on every resize event while a window edge is dragged.
$redrawTimer = New-Object System.Windows.Forms.Timer
$redrawTimer.Interval = 150
$redrawTimer.add_Tick({ $redrawTimer.Stop(); Measure-Dev 'Redraw after resize' { Update-Drawing } })
$twinBox.add_Resize({ $redrawTimer.Stop(); $redrawTimer.Start() })
$chkHidden.add_CheckedChanged({ Update-Drawing })
$chkFolder.add_CheckedChanged({ Update-FolderControls })
$gapBox.add_ValueChanged({ if (-not $script:Syncing) { Update-SettingsFromForm; Update-Preview } })
$thickBox.add_ValueChanged({ if (-not $script:Syncing) { Update-SettingsFromForm; Update-Preview } })
$splitBox.add_ValueChanged({ if (-not $script:Syncing) { Update-SettingsFromForm; Update-Preview } })
$pitchBox.add_ValueChanged({ if (-not $script:Syncing) { Update-SettingsFromForm; Update-GridLocks; Update-Preview } })

$btnMaster.add_Click({ Invoke-Step 'Open master' { [void](Open-InventorAssembly $State.Standard.Master); Write-Log 'Master open (for Set up master / Learn hardware holes); the name box is unchanged.' } })
$btnOpen.add_Click({ Invoke-Step 'Open assembly' { Open-AssemblyFile } })
$btnRead.add_Click({ Invoke-Step 'Read from Inventor' { Read-FromInventor } })
$btnApply.add_Click({ Invoke-Step 'Apply to Inventor' { Invoke-ApplyToInventor } })
$btnReload.add_Click({ Invoke-Step 'Reload file' { Set-Grid (Read-ParamFile $ParamPath); Update-NameFromGrid; Update-Preview } })
$btnLearn.add_Click({ Invoke-Step 'Learn hardware holes' { Invoke-LearnHardwareStep } })
$btnSetup.add_Click({ Invoke-Step 'Set up master' { Invoke-SetupStep } })
$btnDevSave.add_Click({ Invoke-Step 'Save dev log' {
    $dlg = New-Object System.Windows.Forms.SaveFileDialog
    $dlg.Filter = 'Text (*.txt)|*.txt'; $dlg.FileName = 'devlog.txt'
    if ($dlg.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) { Save-DevLog $dlg.FileName; Write-Log "Saved dev log $($dlg.FileName)" }
} })
$btnSvg.add_Click({ Invoke-Step 'Save drawing' { Save-Drawing } })
$btnBrowse.add_Click({ Select-OutputFolder })

$form.add_Shown({
    $split.SplitterDistance = 470
    # Fill the settings without triggering their change events (they would overwrite each other).
    $script:Syncing = $true
    try {
        $gapBox.Value   = [decimal]$State.Settings.minHoleGap
        $thickBox.Value = [decimal][math]::Max(0.1, $State.Settings.thickness)
        $splitBox.Value = [decimal]$State.Settings.splitWidth
        $pitchBox.Value = [decimal]$State.Settings.maxHolePitch
        $viewBox.SelectedIndex = 0
    } finally { $script:Syncing = $false }
    Update-FolderControls
    Invoke-Step 'Load' {
        $State.Standards = Read-Standards $StandardsDir $Root
        $typeBox.Items.Clear()
        foreach ($s in $State.Standards) { [void]$typeBox.Items.Add("$($s.Name)   [$($s.Prefix)...]") }
        Select-Standard $State.Standards[0]
        Set-Grid (Read-ParamFile $ParamPath)
        Update-NameFromGrid
    }
    Write-Log 'Ready. Open or read an assembly when you want to work on one (Inventor is not read at start).'
    Update-Preview
})

$form.add_FormClosing({
    try { Save-FormSettings } catch { }
})

[void]$form.ShowDialog()
