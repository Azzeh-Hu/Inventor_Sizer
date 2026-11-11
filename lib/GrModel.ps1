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
#   - Panel length = profile length + 36 (1580 -> 1616, 1160 -> 1196, 360 -> 396), so a
#     frame's outer size is taken as profile length + 36.
# The exports have no positions, so the layout is inferred: the unit's width is the size all
# frames share, its height the next most shared one, and frames are placed side by side along
# the length in the order they connect. Which face a panel or divider sits on is not in the data.

$GrCornerAdd = 36   # mm added to a profile length for the frame's outer size (2 corners)

# File name without folder and extension, for Windows paths on any system.
function Get-GrLeafName([string]$Path) { [IO.Path]::GetFileNameWithoutExtension(($Path -split '[\\/]')[-1]) }

function Get-GrKind([string]$Name, [string]$File) {
    $base = $(if ($File) { Get-GrLeafName $File } else { ($Name -replace ':\d+$', '') })
    if ($base -match '^APS Ecke') { return 'corner' }
    if ($base -match '^Joint$') { return 'joint' }
    if ($base -match '^P150 ') { return 'frameProfile' }
    if ($base -match '^P\d+ \d') { return 'divider' }
    if ($base -match '(?:^|\s)(FP|RP|TP)-\d') { return 'panel' }
    if ($base -match 'Grundrahmen') { return 'base' }
    if ($base -match 'Jalousie') { return 'damper' }
    if ($base -match 'D.mmstutzen|Stutzen') { return 'stub' }
    'other'
}

function Get-GrLength([string]$Name) {
    $m = [regex]::Match($Name, '(\d+(?:[.,]\d+)?)\s*mm')
    if ($m.Success) { [double]::Parse($m.Groups[1].Value.Replace(',', '.'), $Invariant) } else { 0 }
}

function New-GrComponent([string]$Name, [string]$File) {
    $base = $(if ($File) { Get-GrLeafName $File } else { ($Name -replace ':\d+$', '') })
    [pscustomobject]@{ Name = $Name; File = $File; Base = $base; Kind = (Get-GrKind $Name $File); Length = (Get-GrLength $base) }
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
    $rests = @{}; foreach ($f in $frames) { $rests[$f.Id] = Remove-GrOne $f.Sizes $width }
    $height = Get-GrCommonSize @($frames | ForEach-Object { , $rests[$_.Id] })
    $heightShared = @($frames | Where-Object { $rests[$_.Id] -contains $height }).Count -ge 2
    foreach ($f in $frames) {
        $r = $rests[$f.Id]
        $f.Wy = $(if ($f.Sizes -contains $width) { $width } else { ($f.Sizes | Measure-Object -Maximum).Maximum })
        if ($r.Count -lt 2) { $r = @($r) + @(0) }
        if ($heightShared -and $r -contains $height) { $f.Hz = $height; $f.Lx = (Remove-GrOne $r $height)[0] }
        else { $f.Lx = ($r | Measure-Object -Minimum).Minimum; $f.Hz = ($r | Measure-Object -Maximum).Maximum }
    }

    # Layout: from the frame with most neighbours, place neighbours side by side along the
    # length (away from where we came from); a neighbour with the same length and width but a
    # different height goes on top.
    $root = ($frames | Sort-Object @{ Expression = { $_.Neighbours.Count }; Descending = $true }, Id | Select-Object -First 1)
    $placed = @{ $root.Id = 1 }
    $dirOf = @{ $root.Id = 0 }   # +1 / -1: the side a frame was placed on, relative to its parent
    $queue = New-Object System.Collections.Generic.Queue[int]; $queue.Enqueue($root.Id)
    $outer = { param($v) $v + $GrCornerAdd }
    while ($queue.Count) {
        $a = $byId[$queue.Dequeue()]
        $nextSide = $(if ($dirOf[$a.Id] -ne 0) { $dirOf[$a.Id] } else { 1 })
        foreach ($nid in $a.Neighbours) {
            if ($placed[$nid]) { continue }
            $b = $byId[$nid]
            if ($a.Lx -eq $b.Lx -and $a.Wy -eq $b.Wy -and $a.Hz -ne $b.Hz -and -not ($a.Hz -eq $b.Hz)) {
                $b.X = $a.X; $b.Y = $a.Y; $b.Z = $a.Z + (& $outer $a.Hz); $dirOf[$nid] = $dirOf[$a.Id]
            } else {
                $b.Y = $a.Y; $b.Z = $a.Z
                $b.X = $(if ($nextSide -gt 0) { $a.X + (& $outer $a.Lx) } else { $a.X - (& $outer $b.Lx) })
                $dirOf[$nid] = $nextSide
                if ($dirOf[$a.Id] -eq 0) { $nextSide = -$nextSide }   # the root's 2nd neighbour goes to the other side
            }
            $placed[$nid] = 1; $queue.Enqueue($nid)
        }
    }
    $loose = @($frames | Where-Object { -not $placed[$_.Id] })
    foreach ($f in $loose) {   # frames with no link to the others: in a row after the rest
        $end = ($frames | Where-Object { $placed[$_.Id] } | ForEach-Object { $_.X + (& $outer $_.Lx) } | Measure-Object -Maximum).Maximum
        $f.X = $end + 200; $placed[$f.Id] = 1
        $warnings += "Frame $($f.Id) is not connected to the other frames - drawn separately."
    }
    $minX = ($frames | ForEach-Object { $_.X } | Measure-Object -Minimum).Minimum
    foreach ($f in $frames) { $f.X -= $minX }
    # Number the frames along the length.
    $i = 0; $order = @($frames | Sort-Object X, Z)
    foreach ($f in $order) { $i++; $f | Add-Member NoteProperty No $i -Force }

    $count = { param($kind) @($comps.Values | Where-Object { $_.Kind -eq $kind }).Count }
    [pscustomobject]@{
        Name = $Data.Name; Source = $Data.Source; Frames = $order; Components = $comps
        Width = $width; Height = $height
        Length = (($frames | ForEach-Object { $_.X + (& $outer $_.Lx) }) | Measure-Object -Maximum).Maximum
        OuterWidth = (($frames | ForEach-Object { $_.Y + (& $outer $_.Wy) }) | Measure-Object -Maximum).Maximum
        OuterHeight = (($frames | ForEach-Object { $_.Z + (& $outer $_.Hz) }) | Measure-Object -Maximum).Maximum
        Corners = (& $count 'corner'); Joints = (& $count 'joint'); Unplaced = $unplaced; Warnings = $warnings
        Constraints = $active.Count
    }
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
        "Frame sizes are profile lengths (L $x W $x H); outer size = profile + $GrCornerAdd mm. Width = the size all frames share, height = the next most shared one.",
        'Layout inferred from the constraints (the export has no positions). Panels and dividers are listed per frame; their face is not in the data.'
    )
    $lines + @($Model.Warnings)
}
