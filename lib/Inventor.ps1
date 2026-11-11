# Everything that talks to Autodesk Inventor through COM.

$kPartDocumentObject     = 12290
$kAssemblyDocumentObject = 12291
$DesignTrackingProps     = '{32853F0F-3444-11D1-9E93-0060B03C1CA6}'   # internal name, same in every language
$PartNumberPropId        = 5

# -Start launches Inventor when it is not running (takes a while).
function Get-InventorApp([switch]$Start) {
    try { return [Runtime.InteropServices.Marshal]::GetActiveObject('Inventor.Application') } catch { }
    if (-not $Start) {
        throw ('Autodesk Inventor is not running. If it is open, Inventor and this app must run at the same ' +
               'permission level (both normal, or both "Run as administrator").')
    }
    Write-Log 'Starting Inventor - this can take a minute...'
    $inv = New-Object -ComObject Inventor.Application
    $inv.Visible = $true
    $inv
}

# Opens an assembly in Inventor (starting Inventor if needed), makes it active, returns its name.
function Open-InventorAssembly([string]$Path) {
    if (-not (Test-Path $Path)) { throw "File not found: $Path" }
    if ([IO.Path]::GetExtension($Path) -ne '.iam') { throw "'$Path' is not an assembly (.iam)." }
    $inv = Get-InventorApp -Start
    $doc = $inv.Documents.Open($Path, $true)
    $doc.Activate()
    Write-Log "Opened $Path"
    Read-ActiveAssemblyName
}

# Status for the UI; never throws.
function Get-InventorInfo {
    try {
        $inv = [Runtime.InteropServices.Marshal]::GetActiveObject('Inventor.Application')
        $doc = $inv.ActiveDocument
        if ($null -eq $doc) { return @{ connected = $true; document = ''; isAssembly = $false } }
        @{
            connected  = $true
            document   = [IO.Path]::GetFileNameWithoutExtension($doc.FullFileName)
            isAssembly = ($doc.DocumentType -eq $kAssemblyDocumentObject)
        }
    } catch {
        @{ connected = $false; document = ''; isAssembly = $false }
    }
}

function Get-ActiveAssembly {
    $doc = (Get-InventorApp).ActiveDocument
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

function Get-UnitKind([string]$Unit) {
    switch ($Unit.Trim().ToLowerInvariant()) {
        { $_ -in 'mm', 'cm', 'm', 'in', 'ft' } { return 'length' }
        { $_ -in '', 'ul' }                    { return 'unitless' }
        { $_ -in 'deg', 'rad' }                { return 'angle' }
        default                                { return $_ }
    }
}

# Inventor stores values internally in cm / rad. Setting Parameter.Value with these avoids
# any decimal-comma / decimal-point problem with the Windows language setting.
function ConvertTo-DbValue([double]$Value, [string]$Unit) {
    switch ($Unit.Trim().ToLowerInvariant()) {
        'mm'    { return $Value / 10 }
        'cm'    { return $Value }
        'm'     { return $Value * 100 }
        'in'    { return $Value * 2.54 }
        'ft'    { return $Value * 30.48 }
        'deg'   { return $Value * [math]::PI / 180 }
        default { return $Value }
    }
}

function Push-Parameters($asm, [object[]]$Params) {
    $byName = @{}
    foreach ($p in $Params) { $byName[$p.Name] = $p }
    $used = @{}
    $changed = 0

    foreach ($doc in (Get-ModelDocuments $asm)) {
        if (-not $doc.IsModifiable) { continue }   # library / Content Center / read-only files
        $short = [IO.Path]::GetFileName($doc.FullFileName)
        $plist = $doc.ComponentDefinition.Parameters
        for ($i = 1; $i -le $plist.Count; $i++) {
            $ip = $plist.Item($i)
            if (-not $byName.ContainsKey($ip.Name)) { continue }
            $used[$ip.Name] = $true
            $src = $byName[$ip.Name]
            if ((Get-UnitKind $src.Unit) -ne (Get-UnitKind ([string]$ip.Units))) {
                Write-Log "  $short : $($ip.Name) is '$($ip.Units)' in Inventor but '$($src.Unit)' in the app - skipped"
                continue
            }
            $target = ConvertTo-DbValue $src.Value $src.Unit
            if ([math]::Abs($ip.Value - $target) -lt 1e-9) { continue }   # compare numbers, not text
            try {
                $old = $ip.Expression
                $ip.Value = $target
                $changed++
                Write-Log "  $short : $($ip.Name)  $old -> $(Format-Value $src)"
            } catch {
                Write-Log "  $short : could not set $($ip.Name) ($(Format-Error $_))"
            }
        }
    }

    $missing = @($Params | Where-Object { -not $used.ContainsKey($_.Name) } | ForEach-Object { $_.Name })
    if ($missing.Count) { Write-Log "  note: not used by this model (no parameter with that name): $($missing -join ', ')" }
    if ($changed -eq 0) { Write-Log '  no parameter values changed' }

    # Rebuild parts first, then the assembly so constraints re-solve against the new geometry.
    $docs = Get-ModelDocuments $asm
    for ($i = $docs.Count - 1; $i -ge 0; $i--) {
        if ($docs[$i].IsModifiable) { $docs[$i].Update2($true) }
    }
}

# ------------------------------------------------------------------ inner-panel split
#
# Rule (all types): above the split width the -In part is made half as wide (the part formula
# divides by InSplit) and used twice. The second half is a copy of the first, moved one half
# width towards the middle of the tray and tied to the first with 3 flush constraints on the
# origin planes (2 with offset 0, 1 with the half width). Below the split width the second
# half is deleted again.

$SplitConstraintName = 'InSplit_Offset'

function Get-OccurrencesBySuffix($asm, [string[]]$Suffixes) {
    $found = New-Object System.Collections.Generic.List[object]
    $occs = $asm.ComponentDefinition.Occurrences
    for ($i = 1; $i -le $occs.Count; $i++) {
        $occ = $occs.Item($i)
        try { $file = [IO.Path]::GetFileNameWithoutExtension($occ.Definition.Document.FullFileName) }
        catch { continue }   # suppressed or unresolved
        foreach ($s in $Suffixes) {
            if ($s -and $file.EndsWith($s, [StringComparison]::OrdinalIgnoreCase)) { $found.Add($occ); break }
        }
    }
    , $found.ToArray()
}

function Get-BoxCenter($Box) {
    $a = $Box.MinPoint; $b = $Box.MaxPoint
    , @((($a.X + $b.X) / 2), (($a.Y + $b.Y) / 2), (($a.Z + $b.Z) / 2))
}

function Get-BoxSize($Box) {
    $a = $Box.MinPoint; $b = $Box.MaxPoint
    , @(($b.X - $a.X), ($b.Y - $a.Y), ($b.Z - $a.Z))
}

# Occurrence.CreateGeometryProxy returns the proxy through an out argument; late-bound COM
# needs the argument marked by-ref explicitly.
function Get-GeometryProxy($Occ, $Geometry) {
    $callArgs = [object[]]@($Geometry, $null)
    $mod = New-Object System.Reflection.ParameterModifier(2)
    $mod[1] = $true
    [void]$Occ.GetType().InvokeMember('CreateGeometryProxy', [System.Reflection.BindingFlags]::InvokeMethod,
        $null, $Occ, $callArgs, [System.Reflection.ParameterModifier[]]@($mod), $null, $null)
    if ($null -eq $callArgs[1]) { throw 'Inventor returned no geometry proxy.' }
    $callArgs[1]
}

# Index (0 = X, 1 = Y, 2 = Z) of the part axis the inner panel's width runs along: the axis
# whose extent matches the expected width (cm), unless the standard names it.
function Get-SplitAxis($PartDef, [double]$WidthCm, [string]$Override) {
    $named = [array]::IndexOf(@('X', 'Y', 'Z'), "$Override".ToUpperInvariant())
    if ($named -ge 0) { return $named }
    $size = Get-BoxSize $PartDef.RangeBox
    $diff = @(0, 1, 2 | ForEach-Object { [math]::Abs($size[$_] - $WidthCm) })
    $order = @(0, 1, 2 | Sort-Object { $diff[$_] })
    if ($diff[$order[0]] -gt 0.5 -or ($diff[$order[1]] - $diff[$order[0]]) -lt 0.01) {
        throw ("Cannot tell which direction of the inner panel is its width (part is " +
               (($size | ForEach-Object { Format-Number ($_ * 10) }) -join ' x ') + " mm, expected width $(Format-Number ($WidthCm * 10)) mm). " +
               'Set "splitAxis" to X, Y or Z for this type in the standards file.')
    }
    $order[0]
}

function Find-Constraint($asm, [string]$Name) {
    $cons = $asm.ComponentDefinition.Constraints
    for ($i = 1; $i -le $cons.Count; $i++) { if ($cons.Item($i).Name -eq $Name) { return $cons.Item($i) } }
    $null
}

function Get-Distance($Vector, [double[]]$Target) {
    [math]::Sqrt([math]::Pow($Vector.X - $Target[0], 2) + [math]::Pow($Vector.Y - $Target[1], 2) + [math]::Pow($Vector.Z - $Target[2], 2))
}

# Before the values are pushed: with no split, delete every inner panel after the first.
function Remove-ExtraInnerPanels($asm, $Standard, [int]$Split) {
    $inner = Get-OccurrencesBySuffix $asm (Get-PartSuffixes $Standard 'inner')
    for ($i = $inner.Count - 1; $i -ge [math]::Max($Split, 1); $i--) {
        $name = $inner[$i].Name
        $inner[$i].Delete()
        Write-Log "  removed inner panel $name (not split at this width)"
    }
}

# Before anything is changed: adding a second half only works when the -In part halves itself.
function Test-InnerSplitReady($asm, $Standard, [int]$Split) {
    if ($Split -lt 2) { return }
    $inner = Get-OccurrencesBySuffix $asm (Get-PartSuffixes $Standard 'inner')
    if ($inner.Count -ne 1) { return }
    try { [void]$inner[0].Definition.Parameters.Item('InSplit') }
    catch {
        throw ("This width needs the inner panel split in 2, but $($inner[0].Name) has no InSplit parameter. " +
               'Import the -In setup file from setup-xml into the -In part first (Manage > Parameters > Import from XML).')
    }
}

# After the values are pushed and the parts are rebuilt (the first half already has its new
# width): add or re-position the second half. $InnerWidth is the expected half width in mm.
function Update-InnerSplit($asm, $Standard, [int]$Split, [double]$InnerWidth) {
    if ($Split -lt 2) { return }
    $inner = Get-OccurrencesBySuffix $asm (Get-PartSuffixes $Standard 'inner')
    if ($inner.Count -eq 0) {
        Write-Log "  note: no inner panel found (file name ending in $((Get-PartSuffixes $Standard 'inner') -join ' or ')) - not split"
        return
    }
    $first = $inner[0]
    $link = Find-Constraint $asm $SplitConstraintName
    if ($inner.Count -ge 2 -and -not $link -and -not $inner[1].Grounded) {
        Write-Log '  second inner panel is already in the assembly - its own constraints position it'
        return
    }
    $axis  = Get-SplitAxis $first.Definition ($InnerWidth / 10) $Standard.SplitAxis
    $pitch = (Get-BoxSize $first.Definition.RangeBox)[$axis]   # real width of one half, cm
    if ([math]::Abs($pitch * 10 - $InnerWidth) -gt 0.01) {
        Write-Log ("  warning: the inner panel is $(Format-Number ($pitch * 10)) mm wide in Inventor, the app expected $(Format-Number $InnerWidth) mm. " +
                   'Check the d0 formula of the -In part (InSplit) and the sheet thickness in Settings.')
    }

    # Where the second half belongs: one half width from the first, towards the tray's middle.
    $m = $first.Transformation
    $dir = @($m.Cell(1, $axis + 1), $m.Cell(2, $axis + 1), $m.Cell(3, $axis + 1))
    $outer = Get-OccurrencesBySuffix $asm (Get-PartSuffixes $Standard 'outer')
    $refBox = $(if ($outer.Count) { $outer[0].RangeBox } else { $asm.ComponentDefinition.RangeBox })
    $c0 = Get-BoxCenter $refBox; $c1 = Get-BoxCenter $first.RangeBox
    $dot = ($c0[0] - $c1[0]) * $dir[0] + ($c0[1] - $c1[1]) * $dir[1] + ($c0[2] - $c1[2]) * $dir[2]
    if ([math]::Abs($dot) -lt 0.05) {
        throw ('The inner panel stays in the middle of the tray after halving, so there is no room for the second half. ' +
               'Constrain the -In part to one wall of the -Au part (offset = Clearance), not to its middle.')
    }
    $sign = $(if ($dot -gt 0) { 1 } else { -1 })
    $t = $m.Translation
    $target = [double[]]@(($t.X + $sign * $pitch * $dir[0]), ($t.Y + $sign * $pitch * $dir[1]), ($t.Z + $sign * $pitch * $dir[2]))
    $m.SetTranslation($asm.Parent.TransientGeometry.CreateVector($target[0], $target[1], $target[2]), $false)

    if ($inner.Count -ge 2) {
        if ($link) {
            $link.Offset.Value = $(if ($link.Offset.Value -lt 0) { -$pitch } else { $pitch })
            Write-Log "  second inner panel: offset set to $(Format-Number ($pitch * 10)) mm"
        } else {
            $inner[1].Transformation = $m
            Write-Log "  second inner panel (grounded) moved to $(Format-Number ($pitch * 10)) mm from the first"
        }
        return
    }

    $second = $asm.ComponentDefinition.Occurrences.Add($first.Definition.Document.FullFileName, $m)
    Write-Log "  added inner panel $($second.Name), $(Format-Number ($pitch * 10)) mm from $($first.Name)"
    try {
        $planes = $first.Definition.WorkPlanes   # 1 = YZ, 2 = XZ, 3 = XY: plane n is normal to axis n - 1
        $cons = $asm.ComponentDefinition.Constraints
        for ($j = 0; $j -lt 3; $j++) {
            $p1 = Get-GeometryProxy $first  $planes.Item($j + 1)
            $p2 = Get-GeometryProxy $second $planes.Item($j + 1)
            $c = $cons.AddFlushConstraint($p1, $p2, $(if ($j -eq $axis) { $pitch } else { 0 }))
            if ($j -eq $axis) { $c.Name = $SplitConstraintName; $link = $c }
        }
        $asm.Update()
        if ((Get-Distance $second.Transformation.Translation $target) -gt 0.001) {   # offset ran the other way
            $link.Offset.Value = -$pitch
            $asm.Update()
        }
        if ((Get-Distance $second.Transformation.Translation $target) -gt 0.001) { throw 'the constraints did not put it in the expected place' }
        Write-Log "  mated $($second.Name) to $($first.Name) (3 flush constraints on the origin planes)"
    } catch {
        # Still usable: fix the half in place. The app moves a grounded half on every Apply.
        Write-Log "  warning: could not mate the second inner panel ($($_.Exception.Message)) - grounded in place instead"
        try { $link = Find-Constraint $asm $SplitConstraintName; if ($link) { $link.Delete() } } catch { }
        $second.Transformation = $m
        $second.Grounded = $true
    }
}

# Swap the old size (and project lead) for the new one in the Part Number iProperty (BOMs and
# drawings use it). $FileName, when given, is the new file name: a Part Number that was equal
# to the old file name follows it exactly (e.g. -In -> -In-2T).
function Update-PartNumber($Doc, $Old, $New, [string]$FileName = '') {
    $short = [IO.Path]::GetFileName($Doc.FullFileName)
    try {
        $prop = $Doc.PropertySets.Item($DesignTrackingProps).ItemByPropId($PartNumberPropId)
        $was = [string]$prop.Value
        if ($FileName -and $was -eq [IO.Path]::GetFileNameWithoutExtension($Doc.FullFileName)) { $now = $FileName }
        else { $now = Convert-PanelText $was $Old $New }
        if ($now -ne $was) {
            $prop.Value = $now
            Write-Log "  $short : Part Number $was -> $now"
        } else {
            Write-Log "  $short : Part Number '$was' has no size in it - left as is"
        }
    } catch {
        Write-Log "  $short : could not update the Part Number ($(Format-Error $_))"
    }
}

# Saves the panel under its new name. Every file whose name contains the old size gets the new
# size (and project lead); the inner panel's file always matches the split state (-In when one
# piece, -In-2T when split, for every type), even when the size stays the same. Only files
# whose name changes are written as new files; the rest is saved in place.
# Standard rule: each new file goes into the folder of the file it was made from. Only when
# $OutputFolder is given (chosen in the window for that session) do all new files go there.
function Save-AsNewSize($asm, $Old, $New, [string]$NewAsmName, [string]$OutputFolder, $Standard = $null, [int]$Split = 1) {
    $plan = New-Object System.Collections.Generic.List[object]
    foreach ($doc in (Get-ModelDocuments $asm)) {
        if ($doc.FullFileName -eq $asm.FullFileName -or -not $doc.IsModifiable) { continue }
        $base = [IO.Path]::GetFileNameWithoutExtension($doc.FullFileName)
        if (-not $base.Contains($Old.Token)) { continue }
        $newBase = Convert-PanelText $base $Old $New
        if ($Standard) { $newBase = Convert-SplitName $Standard $newBase $Split }
        if ($newBase -eq $base -and -not $OutputFolder) { continue }
        $dir = $(if ($OutputFolder) { $OutputFolder } else { Split-Path $doc.FullFileName })
        $plan.Add(@{ Doc = $doc; Name = $newBase; Path = (Join-Path $dir ($newBase + [IO.Path]::GetExtension($doc.FullFileName))) })
    }
    $curAsm = [IO.Path]::GetFileNameWithoutExtension($asm.FullFileName)
    if ($NewAsmName -ne $curAsm -or $OutputFolder) {
        $asmDir = $(if ($OutputFolder) { $OutputFolder } else { Split-Path $asm.FullFileName })
        $plan.Add(@{ Doc = $asm; Name = $NewAsmName; Path = (Join-Path $asmDir ($NewAsmName + [IO.Path]::GetExtension($asm.FullFileName))) })
    }

    $existing = @($plan | Where-Object { Test-Path $_.Path } | ForEach-Object { $_.Path })
    if ($existing) { throw "These files already exist, nothing was saved:`n" + ($existing -join "`n") }
    if ($OutputFolder -and -not (Test-Path $OutputFolder)) {
        [void](New-Item -ItemType Directory -Path $OutputFolder)
        Write-Log "  created folder $OutputFolder"
    }

    $inv = $asm.Parent
    $inv.SilentOperation = $true
    try {
        # SaveAs (not copy) re-points the open assembly at the new files; the originals on disk stay as they were.
        foreach ($item in $plan) {
            Update-PartNumber $item.Doc $Old $New $item.Name
            $item.Doc.SaveAs($item.Path, $false)
            Write-Log "  saved $($item.Path)"
        }
        $asm.Save2($true)   # the assembly in place (when its name stays) and sub-assemblies whose references changed
    } finally {
        $inv.SilentOperation = $false
    }
}

function Read-ActiveAssemblyName {
    $asm = Get-ActiveAssembly
    $name = [IO.Path]::GetFileNameWithoutExtension($asm.FullFileName)
    if (-not $SizeRegex.IsMatch($name)) { throw "'$name' does not contain a size like 386x656x47." }
    Write-Log "Active assembly: $($asm.FullFileName)"
    $name
}

# Resizes one panel assembly (any open .iam, not only the active one): split rule, values,
# rebuild, then save in place or as new files. Returns the name the assembly has afterwards.
# Kept free of the window so a later "build a whole box" step can call it once per panel.
function Update-PanelAssembly($asm, $Standards, $Standard, [string]$NewName, [object[]]$Params, $Layout, [string]$OutputFolder) {
    $curName = [IO.Path]::GetFileNameWithoutExtension($asm.FullFileName)
    $old = Split-PanelName $Standards $curName
    $new = Split-PanelName $Standards $NewName
    if ($NewName -ne $curName -and -not $old) {
        throw "The current file name '$curName' has no size in it, so the part files cannot be renamed."
    }

    Test-InnerSplitReady $asm $Standard $Layout.Split
    Write-Log "Updating $curName ..."
    Remove-ExtraInnerPanels $asm $Standard $Layout.Split
    Push-Parameters $asm (@($Params) + @($Layout.Params))
    Update-InnerSplit $asm $Standard $Layout.Split $Layout.InnerWidth
    $asm.Update2($true)

    if (-not $old) {
        $asm.Save2($true)
        Write-Log "Saved $curName in place."
    } elseif ($NewName -eq $curName) {   # same name: always in place, also when another folder is chosen
        Save-AsNewSize $asm $old $new $NewName '' $Standard $Layout.Split   # renames the inner panel if the split state changed
        Write-Log "Saved $curName."
    } else {
        Write-Log "Saving as $NewName ..."
        Save-AsNewSize $asm $old $new $NewName $OutputFolder $Standard $Layout.Split
        Write-Log "Now working on $NewName. The $curName files on disk are unchanged."
    }
    $NewName
}

function Invoke-Apply([string]$Name, [object[]]$Params, $Settings, [string]$ParamPath, $Standards, $Standard) {
    $newName = $Name.Trim()
    $m = $SizeRegex.Match($newName)
    if (-not $m.Success) { throw 'The name must contain a size like 386x656x47.' }
    if ($newName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) { throw "'$newName' is not a valid file name." }

    $values = @{}
    foreach ($p in $Params) { $values[$p.Name] = [double]$p.Value }
    for ($i = 0; $i -lt $SizeParams.Count; $i++) {
        $fromName = [double]::Parse($m.Groups[$i + 1].Value, $Invariant)
        if ($values[$SizeParams[$i]] -ne $fromName) {
            throw "The name says $($SizeParams[$i]) = $(Format-Number $fromName) but the value is $(Format-Number $values[$SizeParams[$i]])."
        }
    }

    $layout = Get-HoleLayout $values $Settings $Standard
    if ($layout.Errors.Count) { throw ("Not applied - fix these first:`n" + ($layout.Errors -join "`n")) }

    $asm = Get-ActiveAssembly

    Save-ParamFile $ParamPath $Params
    Write-Log "Saved values to $ParamPath"
    foreach ($p in $Params) { Write-Log "  $($p.Name) = $(Format-Value $p)" }
    Write-Log ("  holes: length wall {0} (pitch {1} mm), width wall {2}{3}" -f $layout.Length.Holes,
        (Format-Number $layout.Length.Pitch), $layout.Width.Holes, $(if ($layout.Width.MidOn) { ' incl. centre pair' } else { '' }))
    Write-Log ("  inner panel: {0} x {1} mm" -f $layout.Split, (Format-Number $layout.InnerWidth))
    foreach ($n in $layout.Notes) { Write-Log "  note: $n" }

    Update-PanelAssembly $asm $Standards $Standard $newName $Params $layout $Settings.outputFolder
}
