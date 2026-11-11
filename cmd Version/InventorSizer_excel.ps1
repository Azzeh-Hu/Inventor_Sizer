<#
.SYNOPSIS
    InventorSizer (Excel version) - drive an Autodesk Inventor assembly's dimensions from its file name,
    through an Excel parameter sheet, with a small Windows UI.

.DESCRIPTION
    File name  FP-386x656x47  <->  Excel "Parameters" sheet  ->  Inventor parameters
    (assembly + every part/sub-assembly that has a parameter with the same name).

    - Type a new name (e.g. FP-400x700x50) or edit the values in the grid; both stay in sync.
    - "Apply" writes the values to Excel, lets Excel recalculate any formula rows,
      pushes every value into Inventor and rebuilds.
    - If the size in the name changed, the assembly and every file whose name contains the
      old size (386x656x47-Au.ipt, 386x656x47-In.ipt, ...) are saved under the new size.
      The original files are left untouched on disk, so they act as the master template.
      If the size did not change, the files are updated and saved in place.

    Needs only what ships with Windows plus Inventor and Excel (Windows PowerShell 5.1 + COM).
    Start it with Launch_excel.bat. The XML version (InventorSizer.ps1) does not need Excel.
#>
param(
    [string]$ExcelPath = (Join-Path $PSScriptRoot 'Parameters.xlsx')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

# ------------------------------------------------------------------ settings

# Parameters encoded in the file name, in order: FP-<Width>x<Length>x<Height>
$SizeParams = @('Width', 'Length', 'Height')
$SizeRegex  = [regex]'(\d+(?:\.\d+)?)x(\d+(?:\.\d+)?)x(\d+(?:\.\d+)?)'

$kPartDocumentObject     = 12290
$kAssemblyDocumentObject = 12291
$Invariant = [Globalization.CultureInfo]::InvariantCulture
$Current   = [Globalization.CultureInfo]::CurrentCulture

$script:Syncing = $false

# ------------------------------------------------------------------ logging

# Error text with the script line it came from, plus the call chain, so failures can be traced.
function Format-Error($err) {
    $msg = "$($err.Exception.Message) [line $($err.InvocationInfo.ScriptLineNumber): $($err.InvocationInfo.Line.Trim())]"
    $stack = ($err.ScriptStackTrace -split "`r?`n" | ForEach-Object { "      $_" }) -join [Environment]::NewLine
    if ($stack) { $msg += [Environment]::NewLine + $stack }
    $msg
}

function Write-Log([string]$Message) {
    $line = '{0:HH:mm:ss}  {1}' -f (Get-Date), $Message
    if ($script:LogBox) { $script:LogBox.AppendText($line + [Environment]::NewLine) }
    else { Write-Host $line }
}

# ------------------------------------------------------------------ Excel

function New-ParamTemplate($xl, [string]$full) {
    $wb = $xl.Workbooks.Add()
    $ws = $wb.Worksheets.Item(1)
    $ws.Name = 'Parameters'
    # Every cell is written as text through .Formula (Excel turns '386' into a number itself).
    # Writing PowerShell Int32 values cell by cell is what failed with an Int32 -> String cast.
    $rows = @(
        @('Name',      'Value', 'Unit', 'Description'),
        @('Width',     '386',   'mm',   '1st number in the file name'),
        @('Length',    '656',   'mm',   '2nd number in the file name'),
        @('Height',    '47',    'mm',   '3rd number in the file name'),
        @('HoleEdge',  '50',    'mm',   'Hole centre distance from the panel edge (-Au)'),
        @('HoleZ',     '22',    'mm',   'Hole centre height'),
        @('HoleDia',   '4.1',   'mm',   'Hole diameter'),
        @('Clearance', '0.5',   'mm',   'Gap between -In and the -Au wall (assembly offsets)')
    )
    $data = New-Object 'object[,]' $rows.Count, 4
    for ($r = 0; $r -lt $rows.Count; $r++) {
        for ($c = 0; $c -lt 4; $c++) { $data[$r, $c] = [string]$rows[$r][$c] }
    }
    $range = $ws.Range("A1:D$($rows.Count)")
    $range.Formula = $data
    $ws.Range('A1:D1').Font.Bold = $true
    [void]$range.Columns.AutoFit()
    $wb.SaveAs($full, 51)   # 51 = xlOpenXMLWorkbook (.xlsx)
    Write-Log "Created template $full"
    $wb
}

function Open-ParamBook {
    $full = [IO.Path]::GetFullPath($ExcelPath)

    # If the user already has the workbook open in Excel, work in that instance.
    try {
        $xl = [Runtime.InteropServices.Marshal]::GetActiveObject('Excel.Application')
        foreach ($wb in @($xl.Workbooks)) {
            if ($wb.FullName -ieq $full) { return @{ Excel = $xl; Book = $wb; Owned = $false } }
        }
    } catch { }

    $xl = New-Object -ComObject Excel.Application
    $xl.Visible = $false
    $xl.DisplayAlerts = $false
    if (Test-Path $full) { $wb = $xl.Workbooks.Open($full) } else { $wb = New-ParamTemplate $xl $full }
    $h = @{ Excel = $xl; Book = $wb; Owned = $true }
    if ($wb.ReadOnly) {
        Close-ParamBook $h
        throw "$full is locked by another program. Close it and try again."
    }
    $h
}

function Close-ParamBook($h) {
    if (-not $h.Owned) { return }
    $h.Book.Close($false)
    $h.Excel.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($h.Book)
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($h.Excel)
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}

function Read-ParamSheet($wb) {
    $ws = $wb.Worksheets.Item('Parameters')
    $list = New-Object System.Collections.Generic.List[object]
    for ($r = 2; ; $r++) {
        $name = [string]$ws.Cells.Item($r, 1).Value2
        if ([string]::IsNullOrWhiteSpace($name)) { break }
        $cell = $ws.Cells.Item($r, 2)
        $raw = $cell.Value2
        if ($raw -isnot [double]) { throw "Excel row ${r} ($name): value '$raw' is not a number." }
        $list.Add([pscustomobject]@{
            Row         = $r
            Name        = $name.Trim()
            Value       = [double]$raw
            Unit        = ([string]$ws.Cells.Item($r, 3).Value2).Trim()
            Description = [string]$ws.Cells.Item($r, 4).Value2
            IsFormula   = [bool]$cell.HasFormula
        })
    }
    , $list.ToArray()
}

# Optionally writes values (non-formula rows) to Excel, then returns the recalculated sheet.
function Invoke-Excel([object[]]$ToWrite) {
    $h = Open-ParamBook
    try {
        if ($ToWrite) {
            $ws = $h.Book.Worksheets.Item('Parameters')
            foreach ($p in $ToWrite) {
                if (-not $p.IsFormula) { $ws.Cells.Item($p.Row, 2).Value2 = $p.Value }
            }
            $h.Excel.Calculate()
            $h.Book.Save()
        }
        $result = Read-ParamSheet $h.Book
        return , $result
    } finally {
        Close-ParamBook $h
    }
}

# ------------------------------------------------------------------ Inventor

function Get-ActiveAssembly {
    try { $inv = [Runtime.InteropServices.Marshal]::GetActiveObject('Inventor.Application') }
    catch { throw 'Autodesk Inventor is not running.' }
    $doc = $inv.ActiveDocument
    if ($null -eq $doc -or $doc.DocumentType -ne $kAssemblyDocumentObject) {
        throw 'Open the assembly (.iam) in Inventor and make it the active window.'
    }
    $doc
}

# The assembly itself plus every part / sub-assembly it uses, at any depth.
function Get-ModelDocuments($asm) {
    $docs = New-Object System.Collections.Generic.List[object]
    $docs.Add($asm)
    $refs = $asm.AllReferencedDocuments
    for ($i = 1; $i -le $refs.Count; $i++) {
        $d = $refs.Item($i)
        if ($d.DocumentType -eq $kPartDocumentObject -or $d.DocumentType -eq $kAssemblyDocumentObject) {
            $docs.Add($d)
        }
    }
    , $docs.ToArray()
}

function Format-Expression($p) {
    $num = $p.Value.ToString($Current)   # Inventor parses expressions with the Windows locale
    if ($p.Unit) { "$num $($p.Unit)" } else { $num }
}

function Push-Parameters($asm, [object[]]$params) {
    $byName = @{}
    foreach ($p in $params) { $byName[$p.Name] = $p }
    $used = @{}

    foreach ($doc in (Get-ModelDocuments $asm)) {
        $short = [IO.Path]::GetFileName($doc.FullFileName)
        if (-not $doc.IsModifiable) { continue }   # library / Content Center / read-only files
        $plist = $doc.ComponentDefinition.Parameters
        for ($i = 1; $i -le $plist.Count; $i++) {
            $ip = $plist.Item($i)
            if (-not $byName.ContainsKey($ip.Name)) { continue }
            $used[$ip.Name] = $true
            $expr = Format-Expression $byName[$ip.Name]
            if ($ip.Expression -eq $expr) { continue }
            try {
                $old = $ip.Expression
                $ip.Expression = $expr
                Write-Log "  $short : $($ip.Name)  $old -> $expr"
            } catch {
                Write-Log "  $short : could not set $($ip.Name) ($(Format-Error $_))"
            }
        }
    }

    foreach ($p in $params) {
        if (-not $used.ContainsKey($p.Name)) { Write-Log "  note: no Inventor parameter named '$($p.Name)' was found" }
    }

    # Rebuild parts first, then the assembly so constraints re-solve against the new geometry.
    $docs = Get-ModelDocuments $asm
    for ($i = $docs.Count - 1; $i -ge 0; $i--) {
        if ($docs[$i].IsModifiable) { $docs[$i].Update2($true) }
    }
}

# Save the assembly and every file whose name contains $OldToken under the new size.
function Save-AsNewSize($asm, [string]$OldToken, [string]$NewToken, [string]$NewAsmName) {
    $plan = New-Object System.Collections.Generic.List[object]
    foreach ($doc in (Get-ModelDocuments $asm)) {
        if ($doc.FullFileName -eq $asm.FullFileName -or -not $doc.IsModifiable) { continue }
        $name = [IO.Path]::GetFileName($doc.FullFileName)
        if ($name.Contains($OldToken)) {
            $plan.Add(@{ Doc = $doc; Path = Join-Path (Split-Path $doc.FullFileName) ($name.Replace($OldToken, $NewToken)) })
        }
    }
    $asmPath = Join-Path (Split-Path $asm.FullFileName) ($NewAsmName + [IO.Path]::GetExtension($asm.FullFileName))
    $plan.Add(@{ Doc = $asm; Path = $asmPath })

    $existing = @($plan | Where-Object { Test-Path $_.Path } | ForEach-Object { $_.Path })
    if ($existing) { throw "These files already exist, nothing was saved:`n" + ($existing -join "`n") }

    $inv = $asm.Parent
    $inv.SilentOperation = $true
    try {
        # SaveAs (not copy) re-points the open assembly at the new files; the originals on disk stay as they were.
        foreach ($item in $plan) {
            $item.Doc.SaveAs($item.Path, $false)
            Write-Log "  saved $($item.Path)"
        }
        $asm.Save2($true)   # flush sub-assemblies whose references changed
    } finally {
        $inv.SilentOperation = $false
    }
}

# ------------------------------------------------------------------ UI <-> data

$table = New-Object System.Data.DataTable
[void]$table.Columns.Add('Name', [string])
[void]$table.Columns.Add('Value', [double])
[void]$table.Columns.Add('Unit', [string])
[void]$table.Columns.Add('Description', [string])
[void]$table.Columns.Add('Row', [int])
[void]$table.Columns.Add('IsFormula', [bool])

function Format-Number([double]$v) { $v.ToString('0.###', $Invariant) }

function Find-Row([string]$name) {
    foreach ($r in $table.Rows) { if ($r['Name'] -eq $name) { return $r } }
    $null
}

function Set-Grid([object[]]$params) {
    $script:Syncing = $true
    try {
        $table.Rows.Clear()
        foreach ($p in $params) {
            [void]$table.Rows.Add($p.Name, $p.Value, $p.Unit, $p.Description, $p.Row, $p.IsFormula)
        }
        $table.AcceptChanges()
    } finally { $script:Syncing = $false }
}

function Get-GridParams {
    [void]$grid.EndEdit()
    $grid.BindingContext.get_Item($table).EndCurrentEdit()
    $out = foreach ($r in $table.Rows) {
        if ($r['Value'] -is [DBNull]) { throw "Parameter '$($r['Name'])' has no value." }
        [pscustomobject]@{
            Row = $r['Row']; Name = $r['Name']; Value = [double]$r['Value']
            Unit = $r['Unit']; Description = $r['Description']; IsFormula = $r['IsFormula']
        }
    }
    , @($out)
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
        else { $nameBox.Text = "FP-$token" }
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
    }
}

# ------------------------------------------------------------------ actions

function Read-FromInventor {
    $asm = Get-ActiveAssembly
    $name = [IO.Path]::GetFileNameWithoutExtension($asm.FullFileName)
    if (-not $SizeRegex.IsMatch($name)) { throw "'$name' does not contain a size like 386x656x47." }
    $nameBox.Text = $name   # fills the grid through TextChanged
    Write-Log "Active assembly: $($asm.FullFileName)"
}

function Invoke-Apply {
    $newName = $nameBox.Text.Trim()
    $newToken = $SizeRegex.Match($newName).Value
    if (-not $newToken) { throw 'The name must contain a size like 386x656x47.' }
    if ($newName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) { throw "'$newName' is not a valid file name." }

    $asm = Get-ActiveAssembly
    $curName = [IO.Path]::GetFileNameWithoutExtension($asm.FullFileName)
    $curToken = $SizeRegex.Match($curName).Value

    Write-Log 'Writing values to Excel...'
    $params = Invoke-Excel (Get-GridParams)
    Set-Grid $params
    foreach ($p in $params) { Write-Log ("  {0} = {1} {2}" -f $p.Name, (Format-Number $p.Value), $p.Unit) }

    Write-Log "Updating $curName ..."
    Push-Parameters $asm $params

    if ($newName -eq $curName) {
        $asm.Save2($true)
        Write-Log "Saved $curName in place."
    } elseif (-not $curToken) {
        throw "The current file name '$curName' has no size in it, so the part files cannot be renamed."
    } else {
        Write-Log "Saving as $newName ..."
        Save-AsNewSize $asm $curToken $newToken $newName
        Write-Log "Now working on $newName. The $curName files on disk are unchanged."
    }
}

# ------------------------------------------------------------------ form

$form = New-Object System.Windows.Forms.Form
$form.Text = 'Inventor Sizer (Excel)'
$form.Size = New-Object System.Drawing.Size(760, 600)
$form.StartPosition = 'CenterScreen'
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)

$grid = New-Object System.Windows.Forms.DataGridView
$grid.Dock = 'Fill'
$grid.AllowUserToAddRows = $false
$grid.AllowUserToDeleteRows = $false
$grid.AutoSizeColumnsMode = 'Fill'
$grid.RowHeadersVisible = $false
$grid.SelectionMode = 'CellSelect'
$grid.DataSource = $table
$form.Controls.Add($grid)

$top = New-Object System.Windows.Forms.Panel
$top.Dock = 'Top'; $top.Height = 44
$lbl = New-Object System.Windows.Forms.Label
$lbl.Text = 'Assembly name:'; $lbl.AutoSize = $true; $lbl.Location = New-Object System.Drawing.Point(10, 14)
$nameBox = New-Object System.Windows.Forms.TextBox
$nameBox.Location = New-Object System.Drawing.Point(110, 10); $nameBox.Width = 400
$nameBox.Font = New-Object System.Drawing.Font('Segoe UI', 11)
$nameBox.Anchor = 'Top,Left,Right'
$btnRead = New-Object System.Windows.Forms.Button
$btnRead.Text = 'Read from Inventor'; $btnRead.Location = New-Object System.Drawing.Point(520, 9); $btnRead.Size = New-Object System.Drawing.Size(210, 28)
$btnRead.Anchor = 'Top,Right'
$top.Controls.AddRange(@($lbl, $nameBox, $btnRead))
$form.Controls.Add($top)

$bottom = New-Object System.Windows.Forms.Panel
$bottom.Dock = 'Bottom'; $bottom.Height = 190
$buttons = New-Object System.Windows.Forms.FlowLayoutPanel
$buttons.Dock = 'Top'; $buttons.Height = 42; $buttons.Padding = New-Object System.Windows.Forms.Padding(6, 6, 6, 0)
$btnApply = New-Object System.Windows.Forms.Button
$btnApply.Text = 'Apply to Inventor'; $btnApply.Size = New-Object System.Drawing.Size(160, 30)
$btnApply.Font = New-Object System.Drawing.Font('Segoe UI', 9, [System.Drawing.FontStyle]::Bold)
$btnOpenXl = New-Object System.Windows.Forms.Button
$btnOpenXl.Text = 'Open Excel sheet'; $btnOpenXl.Size = New-Object System.Drawing.Size(140, 30)
$btnReload = New-Object System.Windows.Forms.Button
$btnReload.Text = 'Reload from Excel'; $btnReload.Size = New-Object System.Drawing.Size(140, 30)
$buttons.Controls.AddRange(@($btnApply, $btnOpenXl, $btnReload))
$script:LogBox = New-Object System.Windows.Forms.TextBox
$LogBox.Multiline = $true; $LogBox.ReadOnly = $true; $LogBox.ScrollBars = 'Vertical'
$LogBox.Dock = 'Fill'; $LogBox.Font = New-Object System.Drawing.Font('Consolas', 9)
$bottom.Controls.Add($LogBox)
$bottom.Controls.Add($buttons)
$form.Controls.Add($bottom)

# ------------------------------------------------------------------ events

$grid.add_DataBindingComplete({
    foreach ($c in 'Row', 'IsFormula') { if ($grid.Columns[$c]) { $grid.Columns[$c].Visible = $false } }
    foreach ($c in 'Name', 'Unit', 'Description') { if ($grid.Columns[$c]) { $grid.Columns[$c].ReadOnly = $true } }
    foreach ($gr in $grid.Rows) {
        if ($gr.Cells['IsFormula'].Value -eq $true) {
            $gr.ReadOnly = $true
            $gr.DefaultCellStyle.BackColor = [System.Drawing.Color]::Gainsboro
            $gr.DefaultCellStyle.ForeColor = [System.Drawing.Color]::DimGray
        }
    }
})

$grid.add_DataError({ param($s, $e)
    Write-Log "Invalid value in row $($e.RowIndex + 1) - enter a number."
    $e.Cancel = $true
})

$table.add_ColumnChanged({ param($s, $e)
    if (-not $script:Syncing -and $e.Column.ColumnName -eq 'Value' -and $SizeParams -contains $e.Row['Name']) {
        Update-NameFromGrid
    }
})

$nameBox.add_TextChanged({ if (-not $script:Syncing) { Update-GridFromName } })

$btnRead.add_Click({ Invoke-Step 'Read from Inventor' { Read-FromInventor } })
$btnApply.add_Click({ Invoke-Step 'Apply to Inventor' { Invoke-Apply } })
$btnReload.add_Click({ Invoke-Step 'Reload from Excel' { Set-Grid (Invoke-Excel $null); Update-NameFromGrid } })
$btnOpenXl.add_Click({ Invoke-Step 'Open Excel sheet' {
    if (-not (Test-Path $ExcelPath)) { Set-Grid (Invoke-Excel $null) }
    Start-Process $ExcelPath
} })

$form.add_Shown({
    Invoke-Step 'Load Excel' { Set-Grid (Invoke-Excel $null); Update-NameFromGrid }
    try { Read-FromInventor } catch { Write-Log "Inventor: $(Format-Error $_)" }
})

[void]$form.ShowDialog()
