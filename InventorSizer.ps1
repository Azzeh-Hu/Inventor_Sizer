<#
.SYNOPSIS
    InventorSizer - drive an Autodesk Inventor panel assembly from its file name, with hole
    rules, product standards and vector drawings of the result.

.DESCRIPTION
    File name  FP-386x656x47  <->  Parameters.xml  ->  Inventor user parameters
    (assembly + every part that has a parameter with the same name). Model dimensions
    reference those user parameters through formulas set up in Inventor.

    - Type: the product standard (standards\*.json) - name prefix, master assembly, parts.
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
    lib\Inventor.ps1   COM: open, push parameters, rebuild, save as new size, Part Number
    lib\Twin.ps1       drawings: views, bill of materials, axis, window painting, SVG sheet

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

$ParamPath    = Join-Path $Root 'Parameters.xml'
$SettingsPath = Join-Path $Root 'settings.json'
$StandardsDir = Join-Path $Root 'standards'
$FixedParams  = @('HoleDia')   # shown read-only: the hole diameter is always 4.1 mm
$State = @{
    Settings = (Read-Settings $SettingsPath); Standards = @(); Standard = $null
    Values = @{}; Layout = $null; Shapes = $null
}
$script:Syncing = $false

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
    $script:Syncing = $true
    try { $typeBox.SelectedIndex = [array]::IndexOf($State.Standards, $Standard) } finally { $script:Syncing = $false }
    $btnMaster.Enabled = [bool]($Standard.Master -and (Test-Path $Standard.Master))
}

# Picks the type whose prefix matches the name, if there is one.
function Update-StandardFromName {
    $match = Find-StandardForName $State.Standards $nameBox.Text
    if ($match -and $match -ne $State.Standard) {
        Select-Standard $match
        Write-Log "Type: $($match.Name)"
    }
}

# The user picked another type: swap the name prefix.
function Set-StandardFromList {
    $old = $State.Standard
    $new = $State.Standards[$typeBox.SelectedIndex]
    Select-Standard $new
    $name = $nameBox.Text
    if ($old -and $name.StartsWith($old.Prefix, [StringComparison]::OrdinalIgnoreCase)) {
        $nameBox.Text = $new.Prefix + $name.Substring($old.Prefix.Length)
    }
    Update-Preview
}

# ------------------------------------------------------------------ rules + drawing

# Re-runs the hole rules, shows any broken rule, locks Apply, and redraws.
function Update-Preview {
    $State.Values = Get-GridValues
    $errors = @()
    try {
        $State.Layout = Get-HoleLayout $State.Values $State.Settings
        $errors = @($State.Layout.Errors)
    } catch {
        $State.Layout = $null
        $errors = @($_.Exception.Message)
    }
    if (-not $SizeRegex.IsMatch($nameBox.Text)) { $errors += 'The name must contain a size like 386x656x47.' }
    $errLabel.Text = ($errors -join [Environment]::NewLine)
    $errLabel.Visible = $errors.Count -gt 0
    $btnApply.Enabled = $errors.Count -eq 0
    Update-Drawing
}

function Get-CurrentBom {
    if (-not $State.Standard) { return $null }
    Get-BomInfo $State.Standard $State.Values $State.Settings.thickness $nameBox.Text.Trim() $State.Layout
}

function Update-Drawing {
    $size = $twinBox.ClientSize
    $State.Shapes = Get-WindowShapes $State.Values $State.Layout ([string]$viewBox.SelectedItem) $size.Width $size.Height `
                        $chkHidden.Checked (Get-CurrentBom) $State.Settings.thickness
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
}

function Save-FormSettings {
    Update-SettingsFromForm
    $State.Settings = Save-Settings $SettingsPath ([pscustomobject]$State.Settings)
}

function Update-FolderControls {
    $folderBox.Enabled = $chkFolder.Checked
    $btnBrowse.Enabled = $chkFolder.Checked
    $folderInfo.Text = $(if ($chkFolder.Checked) { 'This session only: new files for new sizes go into the folder above.' }
                         else { 'Standard: new files are saved next to the files they are made from.' })
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
        & $Body
        Write-Log 'Done.'
    } catch {
        Write-Log "ERROR: $(Format-Error $_)"
        [void][System.Windows.Forms.MessageBox]::Show($form, $_.Exception.Message, $Title,
            [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
    } finally {
        $form.Enabled = $true
        $form.Cursor = [System.Windows.Forms.Cursors]::Default
        Update-Status
    }
}

function Read-FromInventor {
    $nameBox.Text = Read-ActiveAssemblyName   # fills the grid through TextChanged
}

function Open-AssemblyFile {
    $dlg = New-Object System.Windows.Forms.OpenFileDialog
    $dlg.Filter = 'Inventor assembly (*.iam)|*.iam'
    $dlg.Title = 'Open assembly'
    if ($State.Standard.Master -and (Test-Path $State.Standard.Master)) { $dlg.InitialDirectory = Split-Path $State.Standard.Master }
    if ($dlg.ShowDialog($form) -ne [System.Windows.Forms.DialogResult]::OK) { return }
    $nameBox.Text = Open-InventorAssembly $dlg.FileName
}

function Invoke-ApplyToInventor {
    $settings = Get-ApplySettings
    Save-FormSettings
    $newName = Invoke-Apply $nameBox.Text (Get-GridParams) $settings $ParamPath
    $script:Syncing = $true
    try { $nameBox.Text = $newName } finally { $script:Syncing = $false }
}

function Save-Drawing {
    $dlg = New-Object System.Windows.Forms.SaveFileDialog
    $dlg.Filter = 'SVG drawing (*.svg)|*.svg'
    $dlg.FileName = "$($nameBox.Text.Trim()).svg"
    if ($dlg.ShowDialog($form) -ne [System.Windows.Forms.DialogResult]::OK) { return }
    $shapes = Get-SheetShapes $State.Values $State.Layout (Get-CurrentBom) $State.Settings.thickness $chkHidden.Checked
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
$settingsBox = New-Control 'GroupBox' @{ Dock = 'Bottom'; Height = 172; Text = 'Settings' }
$settingsGrid = New-Control 'TableLayoutPanel' @{ Dock = 'Fill'; ColumnCount = 3; RowCount = 5 }
[void]$settingsGrid.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::AutoSize)))
[void]$settingsGrid.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 100)))
[void]$settingsGrid.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::AutoSize)))
$chkFolder  = New-Control 'CheckBox' @{ Text = 'Save new sizes to another folder (this session only)'; AutoSize = $true; Anchor = 'Left' }
$folderBox  = New-Control 'TextBox' @{ Anchor = 'Left,Right' }
$btnBrowse  = New-Control 'Button' @{ Text = 'Browse...'; AutoSize = $true; Anchor = 'Right' }
$gapBox     = New-Control 'NumericUpDown' @{ Width = 80; DecimalPlaces = 1; Maximum = 1000; Increment = 0.5; Anchor = 'Left' }
$thickBox   = New-Control 'NumericUpDown' @{ Width = 80; DecimalPlaces = 2; Maximum = 50; Increment = 0.1; Anchor = 'Left' }
$thickBox.Minimum = 0.1
$folderInfo = New-Control 'Label' @{ AutoSize = $true; Anchor = 'Left'; ForeColor = [System.Drawing.Color]::DimGray }
$settingsGrid.Controls.Add($chkFolder, 0, 0); $settingsGrid.SetColumnSpan($chkFolder, 3)
$settingsGrid.Controls.Add((New-Control 'Label' @{ Text = 'Folder:'; AutoSize = $true; Anchor = 'Left' }), 0, 1)
$settingsGrid.Controls.Add($folderBox, 1, 1)
$settingsGrid.Controls.Add($btnBrowse, 2, 1)
$settingsGrid.Controls.Add($folderInfo, 0, 2); $settingsGrid.SetColumnSpan($folderInfo, 3)
$settingsGrid.Controls.Add((New-Control 'Label' @{ Text = 'Minimum hole gap (mm):'; AutoSize = $true; Anchor = 'Left' }), 0, 3)
$settingsGrid.Controls.Add($gapBox, 1, 3)
$settingsGrid.Controls.Add((New-Control 'Label' @{ Text = 'Sheet thickness (mm):'; AutoSize = $true; Anchor = 'Left' }), 0, 4)
$settingsGrid.Controls.Add($thickBox, 1, 4)
$settingsBox.Controls.Add($settingsGrid)

$split.Panel1.Controls.Add($grid)
$split.Panel1.Controls.Add($errLabel)
$split.Panel1.Controls.Add($settingsBox)

$twinBox = New-Control 'PictureBox' @{ Dock = 'Fill'; BackColor = [System.Drawing.Color]::White; BorderStyle = 'FixedSingle' }
$twinBar = New-Control 'Panel' @{ Dock = 'Top'; Height = 34 }
$viewBox = New-Control 'ComboBox' @{ DropDownStyle = 'DropDownList'; Location = (New-Point 44 5); Width = 110 }
$viewBox.Items.AddRange([object[]]$TwinViews)
$twinBar.Controls.AddRange(@(
    (New-Control 'Label' @{ Text = 'View:'; AutoSize = $true; Location = (New-Point 4 9) }),
    $viewBox,
    ($chkHidden = New-Control 'CheckBox' @{ Text = 'Show hidden edges'; AutoSize = $true; Location = (New-Point 170 8) }),
    ($btnSvg = New-Control 'Button' @{ Text = 'Save drawing (SVG)...'; Location = (New-Point 310 4); Size = (New-Size 150 26) })
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
    ($statusLabel = New-Control 'Label' @{ AutoSize = $true; Location = (New-Point 590 12); ForeColor = [System.Drawing.Color]::DimGray }),
    (New-Control 'Label' @{ Text = 'Assembly name:'; AutoSize = $true; Location = (New-Point 10 47) }),
    ($nameBox = New-Control 'TextBox' @{ Location = (New-Point 110 43); Width = 330
                                         Font = (New-Object System.Drawing.Font('Segoe UI', 11)) }),
    ($btnOpen = New-Control 'Button' @{ Text = 'Open assembly...'; Location = (New-Point 450 42); Size = (New-Size 120 28) }),
    ($btnRead = New-Control 'Button' @{ Text = 'Read from Inventor'; Location = (New-Point 576 42); Size = (New-Size 140 28) })
))
$form.Controls.Add($top)

$bottom = New-Control 'Panel' @{ Dock = 'Bottom'; Height = 170 }
$buttons = New-Control 'FlowLayoutPanel' @{ Dock = 'Top'; Height = 40; Padding = (New-Object System.Windows.Forms.Padding(6, 6, 6, 0)) }
$buttons.Controls.AddRange(@(
    ($btnApply = New-Control 'Button' @{ Text = 'Apply to Inventor'; Size = (New-Size 160 30)
                                         Font = (New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)) }),
    ($btnOpenFile = New-Control 'Button' @{ Text = 'Open parameter file'; Size = (New-Size 150 30) }),
    ($btnReload = New-Control 'Button' @{ Text = 'Reload file'; Size = (New-Size 110 30) })
))
$LogBox = New-Control 'TextBox' @{ Multiline = $true; ReadOnly = $true; ScrollBars = 'Vertical'; Dock = 'Fill'
                                   Font = (New-Object System.Drawing.Font('Consolas', 9)) }
$bottom.Controls.Add($LogBox)
$bottom.Controls.Add($buttons)
$form.Controls.Add($bottom)

# ------------------------------------------------------------------ events

$grid.add_DataBindingComplete({
    foreach ($c in 'Name', 'Unit', 'Description') { if ($grid.Columns[$c]) { $grid.Columns[$c].ReadOnly = $true } }
    foreach ($gr in $grid.Rows) {
        if ($FixedParams -contains $gr.Cells['Name'].Value) {
            $gr.ReadOnly = $true
            $gr.DefaultCellStyle.ForeColor = [System.Drawing.Color]::Gray
        }
    }
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
$twinBox.add_Paint({ param($s, $e) Invoke-TwinPaint $e.Graphics $State.Shapes })
$twinBox.add_Resize({ Update-Drawing })
$chkHidden.add_CheckedChanged({ Update-Drawing })
$chkFolder.add_CheckedChanged({ Update-FolderControls })
$gapBox.add_ValueChanged({ if (-not $script:Syncing) { Update-SettingsFromForm; Update-Preview } })
$thickBox.add_ValueChanged({ if (-not $script:Syncing) { Update-SettingsFromForm; Update-Preview } })

$btnMaster.add_Click({ Invoke-Step 'Open master' { $nameBox.Text = Open-InventorAssembly $State.Standard.Master } })
$btnOpen.add_Click({ Invoke-Step 'Open assembly' { Open-AssemblyFile } })
$btnRead.add_Click({ Invoke-Step 'Read from Inventor' { Read-FromInventor } })
$btnApply.add_Click({ Invoke-Step 'Apply to Inventor' { Invoke-ApplyToInventor } })
$btnReload.add_Click({ Invoke-Step 'Reload file' { Set-Grid (Read-ParamFile $ParamPath); Update-NameFromGrid; Update-Preview } })
$btnOpenFile.add_Click({ Invoke-Step 'Open parameter file' { Start-Process notepad.exe -ArgumentList "`"$ParamPath`"" } })
$btnSvg.add_Click({ Invoke-Step 'Save drawing' { Save-Drawing } })
$btnBrowse.add_Click({ Select-OutputFolder })

$form.add_Shown({
    $split.SplitterDistance = 470
    # Fill the settings without triggering their change events (they would overwrite each other).
    $script:Syncing = $true
    try {
        $gapBox.Value   = [decimal]$State.Settings.minHoleGap
        $thickBox.Value = [decimal][math]::Max(0.1, $State.Settings.thickness)
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
    try { Read-FromInventor } catch { Write-Log "Inventor: $($_.Exception.Message)" }
    Update-Preview
})

$form.add_FormClosing({
    try { Save-FormSettings } catch { }
})

[void]$form.ShowDialog()
