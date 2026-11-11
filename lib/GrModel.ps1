# GR (Geraeterahmen, the unit frame / box): a model of the frames, profiles, corners, dividers
# and panels of a GR assembly, built from its component names and constraints. Sources:
#   - the iLogic exports (tree + constraints XML, both layouts), or
#   - the active assembly in Inventor.
#
# What the data shows (8185 GR and 6357 2035-N1-ZK750-GR):
#   - Each frame is a cuboid of 8 corners (APS Ecke) and 12 frame profiles (P150 <len>mm),
#     4 of each of its 3 lengths, held together by profile-corner constraints.
#   - Frames touch corner to corner. Neighbours share 2 sizes (side by side along the unit)
#     or 1 (a smaller end section).
#   - Dividers (P350 <len>mm) hang on the frames through Joints; panels (FP/RP/TP) mate to the
#     profiles of one frame with a 2 mm (sometimes 3 mm) gap.
#   - Panel length = profile length + 36 (1580 -> 1616, 1160 -> 1196, 360 -> 396).
#   - Frame outer size = profile length + 2 x 130 (corner cube).
# The exports have no positions, so the layout is inferred: the unit's width is the size all
# frames share, its height the next most shared one, and frames are placed side by side along
# the length in the order they connect. Which face a panel or divider sits on is not in the data.

# Outer frame size = profile length + 2 corners. A P150 profile (70 x 70) is mated to the face
# of an APS corner (130 mm cube) and flush with its outer faces, so each corner adds 130 mm.
$GrCornerAdd = 260
$GrPanelAdd  = 36    # panel length = profile length + 36 (doors + 34)
# Length parameter of each profile kind (deep export: P150 d89 = extrusion, P350 d0).
$GrLengthParams = @{ frameProfile = 'd89'; divider = 'd0' }

# File name without folder and extension, for Windows paths on any system.
function Get-GrLeafName([string]$Path) { [IO.Path]::GetFileNameWithoutExtension(($Path -split '[\\/]')[-1]) }

# Part-name rules: which component is a corner, a frame profile, ... First match wins. They can
# be extended in standards\gr-parts.json (other product lines name their parts differently).
$GrPartRules = @(
    @('corner', '^APS Ecke'), @('joint', '^Joint\d*$'), @('frameProfile', '^P150 '), @('divider', '^P\d+ \d'),
    @('panel', '(?:^|\s)(FP|RP|TP)-\d'), @('base', 'Grundrahmen'), @('damper', 'Jalousie'), @('stub', 'D.mmstutzen|Stutzen')
)

function Read-GrPartRules([string]$Path) {
    if (-not (Test-Path $Path)) { return }
    try {
        $j = Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        $rules = @(foreach ($r in @($j.rules)) { , @([string]$r.kind, [string]$r.pattern) })
        if ($j.lengthParameters) { foreach ($p in $j.lengthParameters.PSObject.Properties) { $script:GrLengthParams[$p.Name] = [string]$p.Value } }
        if ($rules.Count) { $script:GrPartRules = $rules; Write-Log "GR part rules: $($rules.Count) from $Path" }
    } catch { Write-Log "GR part rules in $Path could not be read: $($_.Exception.Message)" }
}

function Get-GrKind([string]$Name, [string]$File) {
    $base = $(if ($File) { Get-GrLeafName $File } else { ($Name -replace ':\d+$', '') })
    foreach ($r in $GrPartRules) { if ($base -match $r[1]) { return $r[0] } }
    'other'
}

function Get-GrLength([string]$Name) {
    $m = [regex]::Match($Name, '(\d+(?:[.,]\d+)?)\s*mm')
    if ($m.Success) { [double]::Parse($m.Groups[1].Value.Replace(',', '.'), $Invariant) } else { 0 }
}

function New-GrComponent([string]$Name, [string]$File) {
    $base = $(if ($File) { Get-GrLeafName $File } else { ($Name -replace ':\d+$', '') })
    # OrigBase: the part as it is in Inventor; Base changes when the GR is edited.
    [pscustomobject]@{ Name = $Name; File = $File; Base = $base; OrigBase = $base; Kind = (Get-GrKind $Name $File); Length = (Get-GrLength $base) }
}

# ------------------------------------------------------------------ reading the exports

# Reads one or more iLogic export files. Returns the top assembly name, its direct components
# and the constraints of the top assembly.
function Read-GrExportFiles([string[]]$Paths) {
    $top = ''; $comps = @{}; $cons = New-Object System.Collections.Generic.List[object]
    foreach ($path in $Paths) {
        $xml = New-Object System.Xml.XmlDocument
        $xml.Load($path)
        $root = $xml.DocumentElement
        switch ($root.Name) {
            'AssemblyTree' {        # <Component><Level/><Tree/><OccurrencePath/><FileName/>...
                foreach ($c in $root.SelectNodes('Component')) {
                    $level = [int]$c.SelectSingleNode('Level').InnerText
                    $name = $c.SelectSingleNode('Tree').InnerText.Trim()
                    if ($level -eq 0) { if (-not $top) { $top = $name }; continue }
                    if ($level -eq 1) { $comps[$name] = New-GrComponent $name $c.SelectSingleNode('FileName').InnerText }
                }
            }
            'AssemblyExport' {      # <Component name level file><Constraints><Constraint .../></Constraints><Components>...
                $t = $root.SelectSingleNode('Component')
                if (-not $top) { $top = $t.GetAttribute('name') }
                foreach ($c in $t.SelectNodes('Components/Component')) { $comps[$c.GetAttribute('name')] = New-GrComponent $c.GetAttribute('name') $c.GetAttribute('file') }
                foreach ($k in $t.SelectNodes('Constraints/Constraint')) {
                    $cons.Add([pscustomobject]@{ Type = $k.GetAttribute('type'); A = $k.GetAttribute('component1'); B = $k.GetAttribute('component2')
                                                 Value = $k.GetAttribute('value'); Suppressed = $k.GetAttribute('suppressed') -eq 'True'; Assembly = $top })
                }
            }
            'AssemblyConstraints' { # <Constraint><Assembly/><Type/><Component1/><Component2/><OffsetAngle/><Suppressed/>
                $asmName = Get-GrLeafName $root.GetAttribute('source')
                if (-not $top) { $top = $asmName }
                foreach ($k in $root.SelectNodes('Constraint')) {
                    if ($k.SelectSingleNode('Assembly').InnerText -ne $asmName) { continue }   # sub-assembly constraints
                    $cons.Add([pscustomobject]@{ Type = $k.SelectSingleNode('Type').InnerText; A = $k.SelectSingleNode('Component1').InnerText
                                                 B = $k.SelectSingleNode('Component2').InnerText; Value = $k.SelectSingleNode('OffsetAngle').InnerText
                                                 Suppressed = $k.SelectSingleNode('Suppressed').InnerText -eq 'True'; Assembly = $asmName })
                }
            }
            default { throw "$([IO.Path]::GetFileName($path)) is not a GR export (root element '$($root.Name)')." }
        }
    }
    if ($cons.Count -eq 0) { throw 'No constraints found. Load the constraints export too (or the export that contains them).' }
    # Components named only in constraints (no tree file loaded): kind from the name.
    foreach ($k in $cons) { foreach ($n in $k.A, $k.B) { if ($n -and -not $comps.ContainsKey($n)) { $comps[$n] = New-GrComponent $n '' } } }
    [pscustomobject]@{ Name = $top; Components = $comps; Constraints = $cons.ToArray(); Source = (($Paths | ForEach-Object { ($_ -split '[\\/]')[-1] }) -join ', ') }
}

# The same data from the active assembly in Inventor.
function Read-GrFromInventor {
    $asm = Get-ActiveAssembly
    $comps = @{}
    foreach ($occ in (Get-OccurrenceList $asm.ComponentDefinition.Occurrences)) {
        $file = Get-Safe { $occ.Definition.Document.FullFileName }
        $comps[$occ.Name] = New-GrComponent $occ.Name ([IO.Path]::GetFileName($file))
    }
    $cons = New-Object System.Collections.Generic.List[object]
    foreach ($c in (Get-OccurrenceList $asm.ComponentDefinition.Constraints)) {
        $cons.Add([pscustomobject]@{ Type = ((Get-ComTypeName $c) -replace 'Constraint$', ''); A = (Get-Safe { $c.OccurrenceOne.Name }); B = (Get-Safe { $c.OccurrenceTwo.Name })
                                     Value = (Get-Safe { $c.Offset.Expression }); Suppressed = [bool](Get-Safe { $c.Suppressed } $false); Assembly = $asm.DisplayName })
    }
    [pscustomobject]@{ Name = [IO.Path]::GetFileNameWithoutExtension($asm.FullFileName); Components = $comps; Constraints = $cons.ToArray(); Source = 'Inventor' }
}

# ------------------------------------------------------------------ building the model

function Get-GrSharedCount([double[]]$A, [double[]]$B) {
    $rest = New-Object System.Collections.Generic.List[double]; foreach ($v in $B) { $rest.Add($v) }
    $n = 0
    foreach ($v in $A) { $i = $rest.IndexOf($v); if ($i -ge 0) { $rest.RemoveAt($i); $n++ } }
    $n
}

function Remove-GrOne([double[]]$Values, [double]$V) {
    $list = New-Object System.Collections.Generic.List[double]; foreach ($x in $Values) { $list.Add($x) }
    $i = $list.IndexOf($V); if ($i -ge 0) { $list.RemoveAt($i) }
    , $list.ToArray()
}

# The size most frames have (ties: the larger one).
function Get-GrCommonSize($SizeLists) {
    $count = @{}
    foreach ($l in $SizeLists) { foreach ($v in ($l | Select-Object -Unique)) { $count[$v] = 1 + [int]$count[$v] } }
    if ($count.Count -eq 0) { return $null }
    ($count.GetEnumerator() | Sort-Object @{ Expression = { $_.Value }; Descending = $true }, @{ Expression = { $_.Key }; Descending = $true } | Select-Object -First 1).Key
}

function Build-GrModel($Data) {
    $comps = $Data.Components
    $kindOf = { param($n) if ($comps.ContainsKey($n)) { $comps[$n].Kind } else { 'other' } }
    $active = @($Data.Constraints | Where-Object { -not $_.Suppressed -and $_.A -and $_.B })

    # Frames: connected groups of corners and frame profiles.
    $adj = @{}
    foreach ($c in $active) {
        $ka = & $kindOf $c.A; $kb = & $kindOf $c.B
        if (($ka -eq 'corner' -and $kb -eq 'frameProfile') -or ($ka -eq 'frameProfile' -and $kb -eq 'corner')) {
            if (-not $adj[$c.A]) { $adj[$c.A] = New-Object System.Collections.Generic.HashSet[string] }
            if (-not $adj[$c.B]) { $adj[$c.B] = New-Object System.Collections.Generic.HashSet[string] }
            [void]$adj[$c.A].Add($c.B); [void]$adj[$c.B].Add($c.A)
        }
    }
    $owner = @{}; $frames = New-Object System.Collections.Generic.List[object]; $warnings = @()
    foreach ($start in ($adj.Keys | Sort-Object)) {
        if ($owner.ContainsKey($start)) { continue }
        $id = $frames.Count + 1
        $stack = New-Object System.Collections.Generic.Stack[string]; $stack.Push($start)
        $members = New-Object System.Collections.Generic.List[string]
        while ($stack.Count) {
            $n = $stack.Pop()
            if ($owner.ContainsKey($n)) { continue }
            $owner[$n] = $id; $members.Add($n)
            foreach ($m in $adj[$n]) { if (-not $owner.ContainsKey($m)) { $stack.Push($m) } }
        }
        $profiles = @($members | Where-Object { $comps[$_].Kind -eq 'frameProfile' })
        $corners = @($members | Where-Object { $comps[$_].Kind -eq 'corner' })
        $byLen = @($profiles | Group-Object { $comps[$_].Length } | Sort-Object { [double]$_.Name })
        $sizes = @(foreach ($g in $byLen) { for ($k = 0; $k -lt [math]::Round($g.Count / 4); $k++) { [double]$g.Name } })
        if ($corners.Count -ne 8 -or $profiles.Count -ne 12 -or $sizes.Count -ne 3) {
            $warnings += "Frame $id is not a closed cuboid ($($corners.Count) corners, $($profiles.Count) profiles) - its sizes are a guess."
            while ($sizes.Count -lt 3) { $sizes += $(if ($sizes.Count) { $sizes[-1] } else { 0 }) }
            $sizes = @($sizes | Select-Object -First 3)
        }
        $frames.Add([pscustomobject]@{
            Id = $id; Sizes = [double[]]$sizes; Corners = $corners; Profiles = $profiles
            Lx = 0.0; Wy = 0.0; Hz = 0.0; X = 0.0; Y = 0.0; Z = 0.0          # profile lengths and outer position (mm)
            Panels = New-Object System.Collections.Generic.List[string]
            Dividers = New-Object System.Collections.Generic.List[string]
            Other = New-Object System.Collections.Generic.List[string]
            Neighbours = New-Object System.Collections.Generic.List[int]
        })
    }
    if ($frames.Count -eq 0) { throw 'No frames found: no constraints between corners (APS Ecke) and frame profiles (P150).' }
    $byId = @{}; foreach ($f in $frames) { $byId[$f.Id] = $f }

    # Frame links: corner-to-corner constraints between 2 frames.
    foreach ($c in $active) {
        $oa = $owner[$c.A]; $ob = $owner[$c.B]
        if ($oa -and $ob -and $oa -ne $ob -and (& $kindOf $c.A) -eq 'corner' -and (& $kindOf $c.B) -eq 'corner') {
            if (-not $byId[$oa].Neighbours.Contains($ob)) { $byId[$oa].Neighbours.Add($ob); $byId[$ob].Neighbours.Add($oa) }
        }
    }

    # Everything else belongs to the frame it is constrained to most often. Items that are only
    # constrained to other items (a divider on a joint, a panel next to a panel) get the frame
    # of those, so the frame is passed along in rounds until nothing changes.
    $assigned = @{}; foreach ($k in $owner.Keys) { $assigned[$k] = $owner[$k] }
    for ($round = 0; $round -lt 10; $round++) {
        $votes = @{}
        foreach ($c in $active) {
            $fa = $assigned[$c.A]; $fb = $assigned[$c.B]
            if ($fa -and -not $fb) { if (-not $votes[$c.B]) { $votes[$c.B] = @{} }; $votes[$c.B][$fa] = 1 + [int]$votes[$c.B][$fa] }
            if ($fb -and -not $fa) { if (-not $votes[$c.A]) { $votes[$c.A] = @{} }; $votes[$c.A][$fb] = 1 + [int]$votes[$c.A][$fb] }
        }
        if ($votes.Count -eq 0) { break }
        foreach ($n in $votes.Keys) { $assigned[$n] = ($votes[$n].GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1).Key }
    }
    $unplaced = @()
    foreach ($n in ($comps.Keys | Sort-Object)) {
        $k = $comps[$n].Kind
        if ($k -in 'corner', 'frameProfile', 'joint') { continue }
        if (-not $assigned[$n]) { $unplaced += $n; continue }
        $f = $byId[$assigned[$n]]
        switch ($k) { 'panel' { $f.Panels.Add($n) } 'divider' { $f.Dividers.Add($n) } default { $f.Other.Add($n) } }
    }

    # Orientation: width = the size most frames share, height = the next most shared one.
    $width = Get-GrCommonSize @($frames | ForEach-Object { , $_.Sizes })
    if ($frames.Count -eq 1) { $width = ($frames[0].Sizes | Sort-Object)[1] }   # one frame: nothing is shared; length = longest, width = middle
    $rests = @{}; foreach ($f in $frames) { $rests[$f.Id] = Remove-GrOne $f.Sizes $width }
    $height = Get-GrCommonSize @($frames | ForEach-Object { , $rests[$_.Id] })
    $heightShared = @($frames | Where-Object { $rests[$_.Id] -contains $height }).Count -ge 2
    if ($frames.Count -eq 1) { $heightShared = $true; $height = ($frames[0].Sizes | Sort-Object)[0] }
    foreach ($f in $frames) {
        $r = $rests[$f.Id]
        $f.Wy = $(if ($f.Sizes -contains $width) { $width } else { ($f.Sizes | Measure-Object -Maximum).Maximum })
        if ($r.Count -lt 2) { $r = @($r) + @(0) }
        if ($heightShared -and $r -contains $height) { $f.Hz = $height; $f.Lx = (Remove-GrOne $r $height)[0] }
        else { $f.Lx = ($r | Measure-Object -Minimum).Minimum; $f.Hz = ($r | Measure-Object -Maximum).Maximum }
    }

    # Placement: from the frame with most neighbours, neighbours go side by side along the
    # length (away from where we came from); a neighbour with the same length and width but a
    # different height goes on top. The relations are kept, so edits re-use them.
    $root = ($frames | Sort-Object @{ Expression = { $_.Neighbours.Count }; Descending = $true }, Id | Select-Object -First 1)
    foreach ($f in $frames) { $f | Add-Member NoteProperty Parent 0 -Force; $f | Add-Member NoteProperty Place 'loose' -Force; $f | Add-Member NoteProperty Dir 1 -Force }
    $root.Place = 'root'; $root.Dir = 0
    $order = New-Object System.Collections.Generic.List[int]; $order.Add($root.Id)
    $queue = New-Object System.Collections.Generic.Queue[int]; $queue.Enqueue($root.Id)
    while ($queue.Count) {
        $a = $byId[$queue.Dequeue()]
        $nextSide = $(if ($a.Dir -ne 0) { $a.Dir } else { 1 })
        foreach ($nid in $a.Neighbours) {
            $b = $byId[$nid]
            if ($b.Place -ne 'loose') { continue }
            $b.Parent = $a.Id
            if ($a.Lx -eq $b.Lx -and $a.Wy -eq $b.Wy -and $a.Hz -ne $b.Hz) { $b.Place = 'stack'; $b.Dir = $a.Dir }
            else {
                $b.Place = 'side'; $b.Dir = $nextSide
                if ($a.Dir -eq 0) { $nextSide = -$nextSide }   # the root's 2nd neighbour goes to the other side
            }
            $order.Add($nid); $queue.Enqueue($nid)
        }
    }
    foreach ($f in $frames) {
        if ($f.Place -eq 'loose') { $order.Add($f.Id); $warnings += "Frame $($f.Id) is not connected to the other frames - drawn separately." }
    }

    # What changes with a frame size when the frame is edited.
    foreach ($n in @($comps.Keys)) {
        $c = $comps[$n]
        $fid = $(if ($assigned[$n]) { [int]$assigned[$n] } else { 0 })
        $c | Add-Member NoteProperty Frame $fid -Force
        $c | Add-Member NoteProperty Follow @() -Force
    }
    foreach ($f in $frames) { Set-GrFollowRules $f $comps }

    $model = [pscustomobject]@{
        Name = $Data.Name; Source = $Data.Source; Frames = $frames; Components = $comps; Order = $order
        Length = 0.0; OuterWidth = 0.0; OuterHeight = 0.0
        Corners = 0; Joints = 0; Unplaced = $unplaced; Warnings = $warnings; Constraints = $active.Count; Edited = $false
        Removed = (New-Object System.Collections.Generic.List[string])   # parts of removed frames that exist in Inventor
    }
    $i = 0
    Update-GrLayout $model
    foreach ($f in @($frames | Sort-Object X, Z)) { $i++; $f | Add-Member NoteProperty No $i -Force }
    Update-GrLayout $model
    $model
}

# A frame profile, divider or panel side follows a frame size when its length is that size plus
# a small constant: frame profiles exactly (4 per size), dividers and panel sides within 60 mm
# (panels + 36, doors + 34, milled dividers + 40). The rest keeps its length when edited.
function Get-GrNearest($Frame, [double]$Len, [double]$Tol) {
    $best = $null
    foreach ($d in @(@('L', $Frame.Lx), @('W', $Frame.Wy), @('H', $Frame.Hz))) {
        $off = $Len - $d[1]
        if ([math]::Abs($off) -le $Tol -and ($null -eq $best -or [math]::Abs($off) -lt [math]::Abs($best.Offset))) { $best = [pscustomobject]@{ Dim = $d[0]; Offset = $off; Part = 0 } }
    }
    $best
}

function Set-GrFollowRules($Frame, $Comps) {
    $quota = @{ L = 4; W = 4; H = 4 }
    foreach ($c in @($Comps.Values | Where-Object { $_.Frame -eq $Frame.Id } | Sort-Object Name)) {
        switch ($c.Kind) {
            'frameProfile' {
                foreach ($d in @(@('L', $Frame.Lx), @('W', $Frame.Wy), @('H', $Frame.Hz))) {
                    if ($c.Length -eq $d[1] -and $quota[$d[0]] -gt 0) { $quota[$d[0]]--; $c.Follow = @([pscustomobject]@{ Dim = $d[0]; Offset = 0; Part = 0 }); break }
                }
            }
            'divider' { $r = Get-GrNearest $Frame $c.Length 60; if ($r) { $c.Follow = @($r) } }
            'panel' {
                $m = $SizeRegex.Match($c.Base)
                if ($m.Success) {
                    $c.Follow = @(foreach ($part in 1, 2) {
                        $r = Get-GrNearest $Frame ([double]::Parse($m.Groups[$part].Value, $Invariant)) 60
                        if ($r) { $r.Part = $part; $r }
                    })
                }
            }
        }
    }
}

# Positions (mm) from the frame sizes and placement relations; also the overall size.
function Update-GrLayout($Model) {
    $byId = @{}; foreach ($f in $Model.Frames) { $byId[$f.Id] = $f }
    $outer = { param($v) $v + $GrCornerAdd }
    $loose = @()
    foreach ($id in $Model.Order) {
        $b = $byId[$id]
        if (-not $b) { continue }
        switch ($b.Place) {
            'root'  { $b.X = 0; $b.Y = 0; $b.Z = 0 }
            'stack' { $a = $byId[$b.Parent]; $b.X = $a.X; $b.Y = $a.Y; $b.Z = $a.Z + (& $outer $a.Hz) }
            'side'  {
                $a = $byId[$b.Parent]; $b.Y = $a.Y; $b.Z = $a.Z
                $b.X = $(if ($b.Dir -gt 0) { $a.X + (& $outer $a.Lx) } else { $a.X - (& $outer $b.Lx) })
            }
            default { $loose += $b }
        }
    }
    foreach ($f in $loose) {
        $end = (@($Model.Frames | Where-Object { $_.Place -ne 'loose' } | ForEach-Object { $_.X + (& $outer $_.Lx) }) + @(0) | Measure-Object -Maximum).Maximum
        $f.X = $end + 200; $f.Y = 0; $f.Z = 0
    }
    $minX = ($Model.Frames | ForEach-Object { $_.X } | Measure-Object -Minimum).Minimum
    foreach ($f in $Model.Frames) { $f.X -= $minX }
    $Model.Length = (($Model.Frames | ForEach-Object { $_.X + (& $outer $_.Lx) }) | Measure-Object -Maximum).Maximum
    $Model.OuterWidth = (($Model.Frames | ForEach-Object { $_.Y + (& $outer $_.Wy) }) | Measure-Object -Maximum).Maximum
    $Model.OuterHeight = (($Model.Frames | ForEach-Object { $_.Z + (& $outer $_.Hz) }) | Measure-Object -Maximum).Maximum
    $Model.Corners = @($Model.Components.Values | Where-Object { $_.Kind -eq 'corner' }).Count
    $Model.Joints = @($Model.Components.Values | Where-Object { $_.Kind -eq 'joint' }).Count
    $Model.Frames = @($Model.Frames | Sort-Object No)
}

# ------------------------------------------------------------------ editing

function Get-GrFrame($Model, [int]$No) {
    $f = $Model.Frames | Where-Object { $_.No -eq $No } | Select-Object -First 1
    if (-not $f) { throw "There is no frame F$No." }
    $f
}

function Get-GrDimValue($Frame, [string]$Dim) { switch ($Dim) { 'L' { $Frame.Lx } 'W' { $Frame.Wy } default { $Frame.Hz } } }
function Set-GrDimValue($Frame, [string]$Dim, [double]$V) { switch ($Dim) { 'L' { $Frame.Lx = $V } 'W' { $Frame.Wy = $V } default { $Frame.Hz = $V } } }

# New length of one component (profile, divider) or one panel side; renames it to match.
function Set-GrComponentLength($Comp, [int]$Part, [double]$Len) {
    $txt = Format-Number $Len
    if ($Comp.Kind -eq 'panel') {
        $m = $SizeRegex.Match($Comp.Base)
        if (-not $m.Success) { return }
        $v = @($m.Groups[1].Value, $m.Groups[2].Value, $m.Groups[3].Value); $v[$Part - 1] = $txt
        $Comp.Base = $Comp.Base.Substring(0, $m.Index) + ($v -join 'x') + $Comp.Base.Substring($m.Index + $m.Length)
    } else {
        $Comp.Length = $Len
        $Comp.Base = [regex]::Replace($Comp.Base, '\d+(?:[.,]\d+)?(?=\s*mm)', $txt, 1)
    }
}

# Changes one size of a frame. With -Shared, every frame that had the same width (or height)
# changes too, so the unit stays one width and height. Profiles, dividers and panel sides that
# follow the size change with it.
function Set-GrFrameSize($Model, [int]$No, [string]$Dim, [double]$Value, [switch]$Shared) {
    if ($Value -le 0) { throw 'A frame size must be greater than 0.' }
    $f = Get-GrFrame $Model $No
    $old = Get-GrDimValue $f $Dim
    if ($old -eq $Value) { return @() }
    $targets = @($f)
    if ($Shared -and $Dim -ne 'L') { $targets = @($Model.Frames | Where-Object { (Get-GrDimValue $_ $Dim) -eq $old }) }
    foreach ($t in $targets) {
        Set-GrDimValue $t $Dim $Value
        foreach ($c in @($Model.Components.Values | Where-Object { $_.Frame -eq $t.Id })) {
            foreach ($r in @($c.Follow | Where-Object { $_.Dim -eq $Dim })) { Set-GrComponentLength $c $r.Part ($Value + $r.Offset) }
        }
    }
    $Model.Edited = $true
    Update-GrLayout $Model
    @($targets | ForEach-Object { "F$($_.No)" })
}

# Swaps two axes of the whole unit (when the length, width or height was guessed wrong).
function Switch-GrAxes($Model, [string]$A, [string]$B) {
    foreach ($f in $Model.Frames) { $va = Get-GrDimValue $f $A; Set-GrDimValue $f $A (Get-GrDimValue $f $B); Set-GrDimValue $f $B $va }
    foreach ($c in $Model.Components.Values) { foreach ($r in @($c.Follow)) { if ($r.Dim -eq $A) { $r.Dim = $B } elseif ($r.Dim -eq $B) { $r.Dim = $A } } }
    Update-GrLayout $Model
}

# A new frame at the end of the unit with the size of frame $LikeNo: 8 corners and 12 frame
# profiles named like the existing ones.
function Add-GrFrame($Model, [int]$LikeNo) {
    $like = Get-GrFrame $Model $LikeNo
    $last = $Model.Frames | Sort-Object { $_.X + $_.Lx } | Select-Object -Last 1
    $id = [int](($Model.Frames | ForEach-Object { $_.Id }) + @(0) | Measure-Object -Maximum).Maximum + 1   # Measure-Object gives a double
    $no = [int](($Model.Frames | ForEach-Object { $_.No }) + @(0) | Measure-Object -Maximum).Maximum + 1
    $f = [pscustomobject]@{
        Id = $id; Sizes = [double[]]@($like.Lx, $like.Wy, $like.Hz); Corners = @(); Profiles = @()
        Lx = $like.Lx; Wy = $like.Wy; Hz = $like.Hz; X = 0.0; Y = 0.0; Z = 0.0
        Panels = New-Object System.Collections.Generic.List[string]; Dividers = New-Object System.Collections.Generic.List[string]
        Other = New-Object System.Collections.Generic.List[string]; Neighbours = New-Object System.Collections.Generic.List[int]
        Parent = $last.Id; Place = 'side'; Dir = 1; No = $no
    }
    $f.Neighbours.Add($last.Id); $last.Neighbours.Add($id)
    $corner = ($Model.Components.Values | Where-Object { $_.Kind -eq 'corner' } | Select-Object -First 1)
    $cornerBase = $(if ($corner) { $corner.Base } else { 'APS Ecke' })
    $prof = ($Model.Components.Values | Where-Object { $_.Kind -eq 'frameProfile' } | Select-Object -First 1)
    $series = $(if ($prof) { ($prof.Base -replace '\d+(?:[.,]\d+)?\s*mm.*$', '') } else { 'P150 ' })
    for ($k = 1; $k -le 8; $k++) {
        $c = New-GrComponent "$($cornerBase):F$no-$k" ''; $c.Kind = 'corner'; $c.Base = $cornerBase; $c.OrigBase = ''   # '' = not in Inventor yet
        $c | Add-Member NoteProperty Frame $id -Force; $c | Add-Member NoteProperty Follow @() -Force
        $Model.Components[$c.Name] = $c
    }
    $k = 0
    foreach ($d in @(@('L', $f.Lx), @('W', $f.Wy), @('H', $f.Hz))) {
        for ($j = 0; $j -lt 4; $j++) {
            $k++
            $base = "$series$(Format-Number $d[1])mm"
            $c = New-GrComponent "$($base):F$no-$k" ''; $c.Kind = 'frameProfile'; $c.Base = $base; $c.Length = $d[1]; $c.OrigBase = ''
            $c | Add-Member NoteProperty Frame $id -Force; $c | Add-Member NoteProperty Follow @([pscustomobject]@{ Dim = $d[0]; Offset = 0; Part = 0 }) -Force
            $Model.Components[$c.Name] = $c
        }
    }
    $Model.Frames = @($Model.Frames) + @($f)
    $Model.Order.Add($id)
    $Model.Edited = $true
    Update-GrLayout $Model
    $f
}

# Removes a frame with everything on it; frames placed against it move to its place.
function Remove-GrFrame($Model, [int]$No) {
    if (@($Model.Frames).Count -le 1) { throw 'A GR needs at least one frame.' }
    $f = Get-GrFrame $Model $No
    foreach ($n in @($Model.Components.Keys)) {
        $c = $Model.Components[$n]
        if ($c.Frame -ne $f.Id) { continue }
        if ($c.OrigBase) { $Model.Removed.Add($n) }
        $Model.Components.Remove($n)
    }
    $children = @($Model.Frames | Where-Object { $_.Parent -eq $f.Id })
    if ($f.Place -eq 'root') {
        if ($children.Count) {
            $newRoot = $children[0]; $newRoot.Place = 'root'; $newRoot.Parent = 0; $newRoot.Dir = 0
            foreach ($c in $children | Select-Object -Skip 1) { $c.Parent = $newRoot.Id }
        }
    } else {
        foreach ($c in $children) { $c.Parent = $f.Parent; if ($c.Place -eq 'side') { $c.Dir = $f.Dir } }
    }
    foreach ($o in $Model.Frames) { [void]$o.Neighbours.Remove($f.Id) }
    $Model.Frames = @($Model.Frames | Where-Object { $_.Id -ne $f.Id })
    [void]$Model.Order.Remove($f.Id)
    # Keep the placement order valid: parents before children.
    $ordered = New-Object System.Collections.Generic.List[int]
    $pending = @($Model.Order)
    while ($pending.Count) {
        $next = @($pending | Where-Object { $p = ($Model.Frames | Where-Object Id -eq $_).Parent; $p -eq 0 -or $ordered.Contains($p) -or -not ($Model.Frames | Where-Object Id -eq $p) })
        if (-not $next.Count) { $next = @($pending[0]) }
        foreach ($n in $next) { $ordered.Add($n) }
        $pending = @($pending | Where-Object { $next -notcontains $_ })
    }
    $Model.Order = $ordered
    $Model.Edited = $true
    Update-GrLayout $Model
}

# ------------------------------------------------------------------ writing back to Inventor

# What an edited GR changes in Inventor: profile and divider occurrences that need a part of
# another length (grouped per new part), panels that need another size, and what cannot be
# written yet (added or removed frames).
function Get-GrPendingChanges($Model) {
    $profiles = foreach ($g in ($Model.Components.Values | Where-Object { $_.Kind -in 'frameProfile', 'divider' -and $_.OrigBase -and $_.Base -ne $_.OrigBase } |
                                Group-Object OrigBase, Base | Sort-Object Name)) {
        $c = $g.Group[0]
        [pscustomobject]@{ Kind = $c.Kind; From = $c.OrigBase; To = $c.Base; Length = $c.Length; Names = @($g.Group | ForEach-Object { $_.Name } | Sort-Object) }
    }
    $panels = foreach ($c in ($Model.Components.Values | Where-Object { $_.Kind -eq 'panel' -and $_.OrigBase -and $_.Base -ne $_.OrigBase } | Sort-Object Name)) {
        [pscustomobject]@{ Name = $c.Name; From = $c.OrigBase; To = $c.Base }
    }
    $blocked = @()
    $added = @($Model.Components.Values | Where-Object { -not $_.OrigBase }).Count
    if ($added) { $blocked += "$added part(s) of added frames are not in Inventor; adding frames in Inventor is not automated yet." }
    if ($Model.Removed.Count) { $blocked += "$($Model.Removed.Count) part(s) of removed frames are still in Inventor; removing frames in Inventor is not automated yet." }
    [pscustomobject]@{ Profiles = @($profiles); Panels = @($panels); Blocked = $blocked }
}

# The length parameter of a profile part: the configured one when it holds the old length,
# otherwise the only model parameter that does.
function Find-GrLengthParameter($Doc, [string]$Kind, [double]$OldLength) {
    $params = $Doc.ComponentDefinition.Parameters
    $want = $OldLength / 10   # Inventor works in cm
    $name = $GrLengthParams[$Kind]
    if ($name) {
        try { $p = $params.Item($name); if ([math]::Abs($p.Value - $want) -lt 1e-6) { return $p } } catch { }
    }
    $hits = @(foreach ($p in (Get-OccurrenceList $params.ModelParameters)) { if ([math]::Abs($p.Value - $want) -lt 1e-6) { $p } })
    if ($hits.Count -eq 1) { return $hits[0] }
    throw "$([IO.Path]::GetFileName($Doc.FullFileName)): no single parameter holds the length $(Format-Number $OldLength) mm (looked for '$name')."
}

# Writes the edited profile lengths into the GR open in Inventor. For every new length a
# copy of the profile part is made next to it (length parameter changed, file named like
# 'P150 1700mm.ipt'; an existing file of that name is used as it is), and the occurrences are
# replaced by it, so their constraints stay. The GR is left open and unsaved for checking.
function Invoke-GrApply($Model) {
    $asm = Get-ActiveAssembly
    $name = [IO.Path]::GetFileNameWithoutExtension($asm.FullFileName)
    if ($name -ne $Model.Name) { throw "Inventor shows '$name', but the GR in the app is '$($Model.Name)'. Open that GR in Inventor first." }
    $todo = Get-GrPendingChanges $Model
    if ($todo.Blocked.Count) { throw ("Not written:`n" + ($todo.Blocked -join "`n") + "`nUndo those (Load the GR again) or make them in Inventor.") }
    if (-not $todo.Profiles.Count -and -not $todo.Panels.Count) { Write-Log 'Nothing to write: no profile or panel sizes changed.'; return }

    $occs = $asm.ComponentDefinition.Occurrences
    $inv = $asm.Parent
    foreach ($g in $todo.Profiles) {
        $first = $null
        try { $first = $occs.ItemByName($g.Names[0]) } catch { throw "Occurrence '$($g.Names[0])' is not in $name." }
        $src = $first.Definition.Document
        $ext = [IO.Path]::GetExtension($src.FullFileName)
        $newPath = Join-Path (Split-Path $src.FullFileName) ($g.To + $ext)
        if (Test-Path $newPath) {
            Write-Log "  $($g.To)$ext exists - used as it is"
        } else {
            $oldLen = Get-GrLength $g.From
            $p = Find-GrLengthParameter $src $g.Kind $oldLen
            $old = $p.Expression
            $inv.SilentOperation = $true
            try {
                $p.Value = $g.Length / 10
                $src.Update()
                $src.SaveAs($newPath, $true)   # a copy; the open part keeps its file name
            } finally {
                $p.Expression = $old           # the original part keeps its length
                $src.Update()
                $inv.SilentOperation = $false
            }
            Write-Log "  created $newPath ($($p.Name) = $(Format-Number $g.Length) mm)"
        }
        foreach ($n in $g.Names) {
            $occs.ItemByName($n).Replace($newPath, $false)
        }
        Write-Log "  $($g.Names.Count) x $($g.From) -> $($g.To)"
        foreach ($n in $g.Names) { $Model.Components[$n].OrigBase = $g.To }
    }
    $asm.Update()
    foreach ($pn in $todo.Panels) { Write-Log "  panel $($pn.Name): needs size $($pn.To) (now $($pn.From)) - make it with the panel tool and replace it" }
    Write-Log "Profiles written into $name. The GR is not saved: check it in Inventor, then save."
}

# ------------------------------------------------------------------ saving

function Save-GrDesign($Model, [string]$Path) {
    $doc = [ordered]@{
        format = 'InventorSizer GR design 1'; name = $Model.Name; source = $Model.Source; saved = (Get-Date).ToString('yyyy-MM-dd HH:mm')
        cornerAdd = $GrCornerAdd; order = @($Model.Order)
        frames = @($Model.Frames | ForEach-Object { [ordered]@{ id = $_.Id; no = $_.No; length = $_.Lx; width = $_.Wy; height = $_.Hz; parent = $_.Parent; place = $_.Place; dir = $_.Dir
                                                                neighbours = @($_.Neighbours); panels = @($_.Panels); dividers = @($_.Dividers); other = @($_.Other) } })
        components = @($Model.Components.Values | Sort-Object Name | ForEach-Object { [ordered]@{ name = $_.Name; file = $_.File; base = $_.Base; origBase = $_.OrigBase; kind = $_.Kind; length = $_.Length; frame = $_.Frame
                                                                                                follow = @($_.Follow | ForEach-Object { [ordered]@{ dim = $_.Dim; offset = $_.Offset; part = $_.Part } }) } })
        warnings = @($Model.Warnings); unplaced = @($Model.Unplaced); removed = @($Model.Removed)
    }
    $doc | ConvertTo-Json -Depth 6 | Set-Content $Path -Encoding UTF8
}

function Read-GrDesign([string]$Path) {
    $j = Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$j.format -notlike 'InventorSizer GR design*') { throw "$([IO.Path]::GetFileName($Path)) is not a GR design saved by this app." }
    $comps = @{}
    foreach ($c in @($j.components)) {
        $orig = $(if ($c.PSObject.Properties.Name -contains 'origBase') { [string]$c.origBase } else { [string]$c.base })
        $comps[[string]$c.name] = [pscustomobject]@{ Name = [string]$c.name; File = [string]$c.file; Base = [string]$c.base; OrigBase = $orig; Kind = [string]$c.kind; Length = [double]$c.length
                                                     Frame = [int]$c.frame; Follow = @(@($c.follow) | Where-Object { $_ } | ForEach-Object { [pscustomobject]@{ Dim = [string]$_.dim; Offset = [double]$_.offset; Part = [int]$_.part } }) }
    }
    $list = { param($items, [string]$type) $l = New-Object "System.Collections.Generic.List[$type]"; foreach ($x in @($items)) { if ($null -ne $x) { $l.Add($x) } }; , $l }
    $frames = @(foreach ($f in @($j.frames)) {
        [pscustomobject]@{ Id = [int]$f.id; No = [int]$f.no; Sizes = [double[]]@($f.length, $f.width, $f.height); Corners = @(); Profiles = @()
                           Lx = [double]$f.length; Wy = [double]$f.width; Hz = [double]$f.height; X = 0.0; Y = 0.0; Z = 0.0
                           Parent = [int]$f.parent; Place = [string]$f.place; Dir = [int]$f.dir
                           Neighbours = (& $list $f.neighbours 'int'); Panels = (& $list $f.panels 'string'); Dividers = (& $list $f.dividers 'string'); Other = (& $list $f.other 'string') }
    })
    $model = [pscustomobject]@{
        Name = [string]$j.name; Source = [IO.Path]::GetFileName($Path); Frames = $frames; Components = $comps; Order = (& $list $j.order 'int')
        Length = 0.0; OuterWidth = 0.0; OuterHeight = 0.0; Corners = 0; Joints = 0
        Unplaced = @($j.unplaced); Warnings = @($j.warnings); Constraints = 0; Edited = $false
        Removed = (& $list $j.removed 'string')
    }
    Update-GrLayout $model
    $model
}

# Cut list as CSV: one row per part name with its length and count.
function Export-GrCutList($Model, [string]$Path) {
    $label = @{ frameProfile = 'Frame profile'; divider = 'Divider'; corner = 'Corner'; joint = 'Joint'; panel = 'Panel'; damper = 'Damper'; stub = 'Duct stub'; base = 'Base frame'; other = 'Other' }
    $rows = foreach ($g in ($Model.Components.Values | Group-Object Kind, Base | Sort-Object { $_.Group[0].Kind }, Name)) {
        $c = $g.Group[0]
        [pscustomobject][ordered]@{ Kind = $label[$c.Kind]; Part = $c.Base; Length = $(if ($c.Kind -in 'frameProfile', 'divider') { [double]$c.Length } else { $null }); Qty = $g.Count
                                    Frames = ((@($g.Group | ForEach-Object { $_.Frame } | Where-Object { $_ } | Select-Object -Unique) | ForEach-Object { $id = $_; "F$(($Model.Frames | Where-Object Id -eq $id).No)" }) -join ' ') }
    }
    Export-CsvLocal @($rows) $Path
}

# ------------------------------------------------------------------ lists

# "1580 x20, 1160 x16" for the components of one kind, longest first.
function Get-GrLengthList($Model, [string]$Kind, [string]$Prefix = '') {
    $items = @($Model.Components.Values | Where-Object { $_.Kind -eq $Kind -and (-not $Prefix -or $_.Base.StartsWith($Prefix)) })
    $x = [char]0x00D7
    $parts = foreach ($g in ($items | Group-Object Base | Sort-Object @{ Expression = { $_.Group[0].Length }; Descending = $true })) {
        $label = ($g.Name -replace '^P\d+\s+', '') -replace 'mm', ''
        "$($label.Trim()) $x$($g.Count)"
    }
    [pscustomobject]@{ Count = $items.Count; Text = ($parts -join ', ') }
}

function Get-GrPanelText($Names, $Components) {
    $x = [char]0x00D7
    $groups = @($Names | ForEach-Object { $Components[$_].Base -replace '^\S+\s+(?=(FP|RP|TP)-)', '' } | Group-Object | Sort-Object Name)
    ($groups | ForEach-Object { $(if ($_.Count -gt 1) { "$($_.Name) $x$($_.Count)" } else { $_.Name }) }) -join ', '
}

# Rows for the drawing's tables: one per frame, then the cut list.
function Get-GrTables($Model) {
    $x = [char]0x00D7
    $frameRows = foreach ($f in $Model.Frames) {
        $content = @()
        if ($f.Panels.Count) { $content += "$($f.Panels.Count) panels" }
        if ($f.Dividers.Count) { $content += "$($f.Dividers.Count) dividers" }
        if ($f.Other.Count) { $content += (@($f.Other | ForEach-Object { $Model.Components[$_].Base -replace '^\S+-3\s+', '' } | Select-Object -Unique) -join ', ') }
        [pscustomobject]@{ Cells = @("F$($f.No)", "$(Format-Number $f.Lx) $x $(Format-Number $f.Wy) $x $(Format-Number $f.Hz)", ($content -join '; ')) }
    }
    $p150 = Get-GrLengthList $Model 'frameProfile'
    $div = Get-GrLengthList $Model 'divider'
    $panels = @($Model.Components.Values | Where-Object { $_.Kind -eq 'panel' })
    $partRows = @(
        [pscustomobject]@{ Cells = @('Frame profiles P150', "$($p150.Count)", $p150.Text) }
        [pscustomobject]@{ Cells = @('Dividers P350', "$($div.Count)", $div.Text) }
        [pscustomobject]@{ Cells = @('Corners (APS Ecke)', "$($Model.Corners)", '') }
        [pscustomobject]@{ Cells = @('Joints', "$($Model.Joints)", '') }
    )
    if ($panels.Count) {
        foreach ($t in 'FP', 'RP', 'TP') {
            $names = @($panels | Where-Object { $_.Base -match "(?:^|\s)$t-\d" } | ForEach-Object { $_.Name })
            if ($names.Count) { $partRows += [pscustomobject]@{ Cells = @("Panels $t", "$($names.Count)", (Get-GrPanelText $names $Model.Components)) } }
        }
    }
    foreach ($k in 'damper', 'stub', 'base', 'other') {
        $items = @($Model.Components.Values | Where-Object { $_.Kind -eq $k })
        if ($items.Count) {
            $label = @{ damper = 'Dampers'; stub = 'Duct stubs'; base = 'Base frame'; other = 'Other' }[$k]
            $partRows += [pscustomobject]@{ Cells = @($label, "$($items.Count)", ((@($items | ForEach-Object { $_.Base }) | Select-Object -Unique) -join ', ')) }
        }
    }
    @{ Frames = @($frameRows); Parts = $partRows }
}

function Get-GrNotes($Model) {
    $x = [char]0x00D7
    $lines = @(
        "$($Model.Name): $($Model.Frames.Count) frame(s), outer about $(Format-Number $Model.Length) $x $(Format-Number $Model.OuterWidth) $x $(Format-Number $Model.OuterHeight) mm (L $x W $x H)",
        "Frame sizes are profile lengths (L $x W $x H); outer size = profile + $GrCornerAdd mm (2 corners of 130). Width = the size all frames share, height = the next most shared one.",
        'Layout inferred from the constraints (the export has no positions). Panels and dividers are listed per frame; their face is not in the data.'
    )
    $lines + @($Model.Warnings)
}
