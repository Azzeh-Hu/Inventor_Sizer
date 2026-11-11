# Exports an assembly's component tree, constraints, joints and patterns, so a box can be
# analysed and later rebuilt by the app. Writes, next to the chosen base path:
#
#   <base>-tree.csv         one row per occurrence at any depth: position, rotation, size, length
#   <base>-parts.csv        one row per file: how often it is used, its size, its user parameters
#   <base>-constraints.csv  one row per constraint / joint: type, the 2 occurrences and the
#                           geometry they connect (plane, face normal, axis, named entity)
#   <base>.json             all of the above in one file (send this one for analysis)
#
# CSV files use the Windows list separator and decimal sign, so Excel opens them directly.

Add-Type -AssemblyName Microsoft.VisualBasic

function Get-ComTypeName($Object) {
    if ($null -eq $Object) { return '' }
    try { [Microsoft.VisualBasic.Information]::TypeName($Object) } catch { $Object.GetType().Name }
}

function Get-Safe([scriptblock]$Get, $Default = '') { try { $v = & $Get; if ($null -eq $v) { $Default } else { $v } } catch { $Default } }

function Format-Mm([double]$Cm) { [math]::Round($Cm * 10, 3) }

# iLogic entity name ("Names" tab of the iLogic editor), if the face / edge / plane has one.
function Get-EntityName($Entity) {
    $native = Get-Safe { $Entity.NativeObject } $Entity
    foreach ($obj in @($Entity, $native)) {
        $name = Get-Safe {
            $set = $obj.AttributeSets.Item('iLogicEntityNameSet')
            $set.Item('iLogicEntityName').Value
        }
        if ($name) { return [string]$name }
    }
    ''
}

# Text that says which geometry a constraint uses, with enough numbers to find it again.
function Get-EntityText($Entity) {
    if ($null -eq $Entity) { return '' }
    $type = (Get-ComTypeName $Entity) -replace 'Proxy$', ''
    $parts = @($type)
    $name = Get-EntityName $Entity
    if ($name) { $parts += "name=$name" }
    $wpName = Get-Safe { (Get-Safe { $Entity.NativeObject } $Entity).Name }
    if ($wpName -and $type -match '^Work') { $parts += "'$wpName'" }
    $g = Get-Safe { $Entity.Geometry } $null
    if ($g) {
        $n = Get-Safe { $g.Normal } $null
        $root = Get-Safe { $g.RootPoint } $null
        $axis = Get-Safe { $g.AxisVector } $null
        $base = Get-Safe { $g.BasePoint } $null
        $r = Get-Safe { $g.Radius } $null
        if ($n -and $root) { $parts += ('normal ({0}, {1}, {2}) through ({3}, {4}, {5}) mm' -f [math]::Round($n.X, 3), [math]::Round($n.Y, 3), [math]::Round($n.Z, 3), (Format-Mm $root.X), (Format-Mm $root.Y), (Format-Mm $root.Z)) }
        elseif ($axis -and $base) { $parts += ('axis ({0}, {1}, {2}) through ({3}, {4}, {5}) mm' -f [math]::Round($axis.X, 3), [math]::Round($axis.Y, 3), [math]::Round($axis.Z, 3), (Format-Mm $base.X), (Format-Mm $base.Y), (Format-Mm $base.Z)) }
        if ($r) { $parts += "radius $(Format-Mm $r) mm" }
    }
    $parts -join ' '
}

function Get-OccurrenceRow($Occ, [int]$Level, [string]$Parent) {
    $path = $(if ($Parent) { "$Parent/$($Occ.Name)" } else { $Occ.Name })
    $row = [ordered]@{
        Level = $Level; Path = $path; Occurrence = $Occ.Name; File = ''; Folder = ''; PartNumber = ''; Description = ''
        Suppressed = [bool](Get-Safe { $Occ.Suppressed } $false); Grounded = [bool](Get-Safe { $Occ.Grounded } $false)
        PatternElement = [bool](Get-Safe { $Occ.IsPatternElement } $false)
        SizeX = $null; SizeY = $null; SizeZ = $null; Length = $null
        PosX = $null; PosY = $null; PosZ = $null; XDir = ''; YDir = ''; ZDir = ''
    }
    if ($row.Suppressed) { return [pscustomobject]$row }
    $doc = Get-Safe { $Occ.Definition.Document } $null
    if ($doc) {
        $row.File = [IO.Path]::GetFileName($doc.FullFileName); $row.Folder = Split-Path $doc.FullFileName
        $row.PartNumber  = Get-Safe { $doc.PropertySets.Item($DesignTrackingProps).ItemByPropId($PartNumberPropId).Value }
        $row.Description = Get-Safe { $doc.PropertySets.Item($DesignTrackingProps).ItemByPropId(29).Value }
    }
    $box = Get-Safe { $Occ.Definition.RangeBox } $null
    if ($box) {
        $row.SizeX = Format-Mm ($box.MaxPoint.X - $box.MinPoint.X)
        $row.SizeY = Format-Mm ($box.MaxPoint.Y - $box.MinPoint.Y)
        $row.SizeZ = Format-Mm ($box.MaxPoint.Z - $box.MinPoint.Z)
        $row.Length = ($row.SizeX, $row.SizeY, $row.SizeZ | Measure-Object -Maximum).Maximum
    }
    $m = Get-Safe { $Occ.Transformation } $null
    if ($m) {
        $t = $m.Translation
        $row.PosX = Format-Mm $t.X; $row.PosY = Format-Mm $t.Y; $row.PosZ = Format-Mm $t.Z
        $col = { param($c) '({0}, {1}, {2})' -f [math]::Round($m.Cell(1, $c), 4), [math]::Round($m.Cell(2, $c), 4), [math]::Round($m.Cell(3, $c), 4) }
        $row.XDir = & $col 1; $row.YDir = & $col 2; $row.ZDir = & $col 3
    }
    [pscustomobject]$row
}

function Get-TreeRows($Occurrences, [int]$Level, [string]$Parent, $Rows) {
    for ($i = 1; $i -le $Occurrences.Count; $i++) {
        $occ = $Occurrences.Item($i)
        $row = Get-OccurrenceRow $occ $Level $Parent
        $Rows.Add($row)
        if (-not $row.Suppressed) {
            $subs = Get-Safe { $occ.SubOccurrences } $null
            if ($subs -and $subs.Count -gt 0) { Get-TreeRows $subs ($Level + 1) $row.Path $Rows }
        }
    }
}

function Get-UserParameterText($Doc) {
    $list = Get-Safe { $Doc.ComponentDefinition.Parameters.UserParameters } $null
    if (-not $list) { return '' }
    @(for ($i = 1; $i -le $list.Count; $i++) { $p = $list.Item($i); "$($p.Name)=$($p.Expression)" }) -join '; '
}

# Constraints, joints and patterns of one assembly definition.
function Get-RelationRows($AsmDoc, $Rows, $Patterns) {
    $def = $AsmDoc.ComponentDefinition
    $asmName = [IO.Path]::GetFileName($AsmDoc.FullFileName)
    $cons = Get-Safe { $def.Constraints } $null
    if ($cons) {
        for ($i = 1; $i -le $cons.Count; $i++) {
            $c = $cons.Item($i)
            $Rows.Add([pscustomobject][ordered]@{
                Assembly = $asmName; Kind = 'constraint'; Name = $c.Name; Type = ((Get-ComTypeName $c) -replace 'Constraint$', '')
                Suppressed = [bool](Get-Safe { $c.Suppressed } $false)
                OccurrenceOne = Get-Safe { $c.OccurrenceOne.Name }; OccurrenceTwo = Get-Safe { $c.OccurrenceTwo.Name }
                EntityOne = Get-EntityText (Get-Safe { $c.EntityOne } $null); EntityTwo = Get-EntityText (Get-Safe { $c.EntityTwo } $null)
                Offset = Get-Safe { $c.Offset.Expression }; Angle = Get-Safe { $c.Angle.Expression }
            })
        }
    }
    $joints = Get-Safe { $def.Joints } $null
    if ($joints) {
        for ($i = 1; $i -le $joints.Count; $i++) {
            $j = $joints.Item($i)
            $Rows.Add([pscustomobject][ordered]@{
                Assembly = $asmName; Kind = 'joint'; Name = $j.Name; Type = "joint type $(Get-Safe { $j.Definition.JointType })"
                Suppressed = [bool](Get-Safe { $j.Suppressed } $false)
                OccurrenceOne = Get-Safe { $j.OccurrenceOne.Name }; OccurrenceTwo = Get-Safe { $j.OccurrenceTwo.Name }
                EntityOne = Get-EntityText (Get-Safe { $j.Definition.OriginOne.Entity } $null); EntityTwo = Get-EntityText (Get-Safe { $j.Definition.OriginTwo.Entity } $null)
                Offset = ''; Angle = ''
            })
        }
    }
    $pats = Get-Safe { $def.OccurrencePatterns } $null
    if ($pats) {
        for ($i = 1; $i -le $pats.Count; $i++) {
            $p = $pats.Item($i)
            $Patterns.Add([pscustomobject][ordered]@{
                Assembly = $asmName; Name = $p.Name; Type = Get-ComTypeName $p
                Elements = Get-Safe { $p.OccurrencePatternElements.Count }
                Count1 = Get-Safe { $p.ColumnCount.Expression }; Spacing1 = Get-Safe { $p.ColumnOffset.Expression }
                Count2 = Get-Safe { $p.RowCount.Expression }; Spacing2 = Get-Safe { $p.RowOffset.Expression }
            })
        }
    }
}

function Export-CsvLocal($Rows, [string]$Path) {
    $sep = (Get-Culture).TextInfo.ListSeparator
    $culture = Get-Culture
    $out = foreach ($r in $Rows) {
        $o = [ordered]@{}
        foreach ($p in $r.PSObject.Properties) { $o[$p.Name] = $(if ($p.Value -is [double]) { $p.Value.ToString('0.###', $culture) } else { $p.Value }) }
        [pscustomobject]$o
    }
    $out | Export-Csv -Path $Path -NoTypeInformation -Delimiter $sep -Encoding UTF8
}

# $BasePath: path without extension, e.g. C:\temp\6357 2035-N1-ZK750-GR
function Export-AssemblyTree($Asm, [string]$BasePath) {
    $rows = New-Object System.Collections.Generic.List[object]
    Get-TreeRows $Asm.ComponentDefinition.Occurrences 0 '' $rows

    # Every assembly in the tree (the top one and each sub-assembly file once) for its relations.
    $relations = New-Object System.Collections.Generic.List[object]
    $patterns = New-Object System.Collections.Generic.List[object]
    $asmDocs = @($Asm) + @(Get-ModelDocuments $Asm | Where-Object { $_.DocumentType -eq $kAssemblyDocumentObject -and $_.FullFileName -ne $Asm.FullFileName })
    foreach ($d in $asmDocs) { Get-RelationRows $d $relations $patterns }

    $parts = foreach ($g in ($rows | Where-Object { $_.File } | Group-Object File)) {
        $first = $g.Group[0]
        $doc = Get-Safe { ($Asm.Parent.Documents.ItemByName((Join-Path $first.Folder $first.File))) } $null
        [pscustomobject][ordered]@{
            File = $g.Name; Family = ($g.Name -replace '\d+(?:[.,]\d+)?', '#'); Qty = $g.Count
            Length = $first.Length; SizeX = $first.SizeX; SizeY = $first.SizeY; SizeZ = $first.SizeZ
            PartNumber = $first.PartNumber; Description = $first.Description; Folder = $first.Folder
            UserParameters = $(if ($doc) { Get-UserParameterText $doc } else { '' })
        }
    }

    Export-CsvLocal $rows "$BasePath-tree.csv"
    Export-CsvLocal @($parts) "$BasePath-parts.csv"
    Export-CsvLocal $relations "$BasePath-constraints.csv"
    [ordered]@{
        assembly = $Asm.FullFileName; exported = (Get-Date).ToString('yyyy-MM-dd HH:mm'); units = 'mm'
        occurrences = $rows; parts = @($parts); relations = $relations; patterns = $patterns
    } | ConvertTo-Json -Depth 5 | Set-Content "$BasePath.json" -Encoding UTF8

    Write-Log "Exported $($rows.Count) occurrence(s), $(@($parts).Count) file(s), $($relations.Count) constraint(s)/joint(s), $($patterns.Count) pattern(s):"
    foreach ($s in '-tree.csv', '-parts.csv', '-constraints.csv', '.json') { Write-Log "  $BasePath$s" }
}
