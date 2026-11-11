# Drawings of a GR (unit frame) model from lib\GrModel.ps1: frames as profile wireframes with
# corners, frame labels, overall dimensions, a frame table and a cut list.
# Drawing axes as in Twin.ps1: x = width, y = length (along the unit), z = height.

function Get-GrBoxes($Model) {
    @(foreach ($f in $Model.Frames) {
        $a = $GrCornerAdd
        [pscustomobject]@{ F = $f; X0 = $f.Y; X1 = $f.Y + $f.Wy + $a; Y0 = $f.X; Y1 = $f.X + $f.Lx + $a; Z0 = $f.Z; Z1 = $f.Z + $f.Hz + $a }
    })
}

function Get-GrCornersOf($b) {
    , @(@($b.X0, $b.Y0, $b.Z0), @($b.X1, $b.Y0, $b.Z0), @($b.X1, $b.Y1, $b.Z0), @($b.X0, $b.Y1, $b.Z0),
        @($b.X0, $b.Y0, $b.Z1), @($b.X1, $b.Y0, $b.Z1), @($b.X1, $b.Y1, $b.Z1), @($b.X0, $b.Y1, $b.Z1))
}

$GrEdges = @(@(0, 1), @(1, 2), @(2, 3), @(3, 0), @(4, 5), @(5, 6), @(6, 7), @(7, 4), @(0, 4), @(1, 5), @(2, 6), @(3, 7))

function Add-GrFrames($List, $T, $Boxes) {
    foreach ($b in $Boxes) {
        $c = Get-GrCornersOf $b
        foreach ($e in $GrEdges) { Add-Poly3 $List 'profile' $T @($c[$e[0]], $c[$e[1]]) $false }
        foreach ($p in $c) {
            $s = Get-Screen $T $p[0] $p[1] $p[2]
            Add-Rect $List 'corner' ($s[0] - 2.5) ($s[1] - 2.5) ($s[0] + 2.5) ($s[1] + 2.5)
        }
    }
}

# Dividers (P350 / P350 milled, 'gefraest') and joints.
# - Read from Inventor: drawn where they really are (range box inside their frame), as a line
#   along the divider and a square for each joint, like the frame profiles and corners. They
#   follow edits: a divider that follows a frame size stretches with it, a moved divider shifts
#   by its change in offset.
# - From export files (no positions): only dividers placed by an offset, drawn schematically
#   through the middle of the frame.
# Returned in GR coordinates (0 = length, 1 = width, 2 = height), relative to the unit.

function Get-GrDimIndex([string]$Dim) { @{ L = 0; W = 1; H = 2 }[$Dim] }

function Get-GrFramePos($Frame) { [double[]]@($Frame.X, $Frame.Y, $Frame.Z) }
function Get-GrFrameSize($Frame) { [double[]]@($Frame.Lx, $Frame.Wy, $Frame.Hz) }

function Get-GrDividerKind($Comp) {
    if ($null -ne $Comp.Position -and $Comp.Position -ne $Comp.OrigPosition) { return 'dividerMoved' }
    if ($Comp.Base -match 'gefr') { 'dividerMilled' } else { 'divider' }
}

function Get-GrDividerLines($Model) {
    $byId = @{}; foreach ($f in $Model.Frames) { $byId[$f.Id] = $f }
    $half = $GrCornerAdd / 2
    foreach ($c in @($Model.Components.Values | Where-Object { $_.Kind -eq 'divider' })) {
        $f = $byId[$c.Frame]
        if (-not $f) { continue }
        $o = Get-GrFramePos $f; $size = Get-GrFrameSize $f
        if ($c.PSObject.Properties.Name -contains 'Geo' -and $c.Geo) {
            $mn = [double[]]$c.Geo.Min.Clone(); $mx = [double[]]$c.Geo.Max.Clone()
            $ext = @(0, 1, 2 | ForEach-Object { $mx[$_] - $mn[$_] })
            $k = [array]::IndexOf($ext, ($ext | Measure-Object -Maximum).Maximum)   # along the divider
            foreach ($r in @($c.Follow)) {                                          # stretches with the frame size it follows
                $i = Get-GrDimIndex $r.Dim
                if ($i -eq $k) { $mx[$k] += $size[$k] - $c.Geo.Size0[$k] }
            }
            if ($null -ne $c.Position -and $null -ne $c.Geo.Pos0 -and $c.Position -ne $c.Geo.Pos0) {   # moved: shift across, along the offset axis
                $partner = $Model.Components[[string]$c.PosPartner]
                $pk = $(if ($partner -and @($partner.Follow).Count) { Get-GrDimIndex $partner.Follow[0].Dim } else { -1 })
                $s = @(0, 1, 2 | Where-Object { $_ -ne $k -and $_ -ne $pk }) | Select-Object -First 1
                $mn[$s] += $c.Position - $c.Geo.Pos0; $mx[$s] += $c.Position - $c.Geo.Pos0
            }
            $p0 = [double[]]@(0, 1, 2 | ForEach-Object { $o[$_] + ($mn[$_] + $mx[$_]) / 2 }); $p1 = [double[]]$p0.Clone()
            $p0[$k] = $o[$k] + $mn[$k]; $p1[$k] = $o[$k] + $mx[$k]
        } elseif ($null -ne $c.Position) {
            $partner = $Model.Components[[string]$c.PosPartner]
            $pdim = $(if ($partner -and @($partner.Follow).Count) { [string]$partner.Follow[0].Dim } else { '' })
            $sz = @{ L = $f.Lx; W = $f.Wy; H = $f.Hz }
            if (@($c.Follow).Count) { $dim = [string]$c.Follow[0].Dim; $len = $null }
            else {
                $dim = @('H', 'W', 'L' | Where-Object { $_ -ne $pdim -and $sz[$_] -ge $c.Length }) | Select-Object -First 1
                if (-not $dim) { continue }
                $len = $c.Length
            }
            $k = Get-GrDimIndex $dim
            $s = Get-GrDimIndex (@('L', 'H', 'W' | Where-Object { $_ -ne $dim -and $_ -ne $pdim }) | Select-Object -First 1)
            $p0 = [double[]]@(0, 1, 2 | ForEach-Object { $o[$_] + ($size[$_] + $GrCornerAdd) / 2 }); $p1 = [double[]]$p0.Clone()
            $p0[$s] = $o[$s] + $half + [double]$c.Position; $p1[$s] = $p0[$s]
            $p0[$k] = $o[$k] + $half; $p1[$k] = $(if ($null -ne $len) { $p0[$k] + $len } else { $o[$k] + $size[$k] + $half })
        } else { continue }
        [pscustomobject]@{ A = $p0; B = $p1; Axis = $k; Name = $c.Name; Kind = (Get-GrDividerKind $c); Frame = $f; Real = [bool]($c.PSObject.Properties.Name -contains 'Geo' -and $c.Geo) }
    }
}

function Get-GrJointPoints($Model) {
    $byId = @{}; foreach ($f in $Model.Frames) { $byId[$f.Id] = $f }
    foreach ($c in @($Model.Components.Values | Where-Object { $_.Kind -eq 'joint' -and $_.PSObject.Properties.Name -contains 'Geo' -and $_.Geo })) {
        $f = $byId[$c.Frame]
        if (-not $f) { continue }
        $o = Get-GrFramePos $f
        , [double[]]@(0, 1, 2 | ForEach-Object { $o[$_] + ($c.Geo.Min[$_] + $c.Geo.Max[$_]) / 2 })
    }
}

# GR coordinates -> drawing coordinates (x = width, y = length, z = height).
function ConvertTo-GrDraw([double[]]$P) { , [double[]]@($P[1], $P[0], $P[2]) }

# Spacing between subsequent dividers: per frame and direction, the dividers' positions across
# their length, from the inner face of the frame profile (70 mm) to each divider and on to the
# far profile, as a chain of dimensions just past the dividers' ends.
function Add-GrDividerChains($List, $T, $Lines) {
    $prof = 70
    foreach ($g in ($Lines | Group-Object { "$($_.Frame.Id)|$($_.Axis)" })) {
        $ls = @($g.Group); $k = $ls[0].Axis; $f = $ls[0].Frame
        $o = Get-GrFramePos $f; $size = Get-GrFrameSize $f
        $others = @(0, 1, 2 | Where-Object { $_ -ne $k })
        $spread = @($others | ForEach-Object { $i = $_; (($ls | ForEach-Object { $_.A[$i] }) | Measure-Object -Maximum).Maximum - (($ls | ForEach-Object { $_.A[$i] }) | Measure-Object -Minimum).Minimum })
        $s = $(if ($spread[0] -ge $spread[1]) { $others[0] } else { $others[1] })
        if ($ls.Count -eq 1) {   # one divider: the axis it sits furthest from the middle on
            $mid = @($others | ForEach-Object { [math]::Abs($ls[0].A[$_] - ($o[$_] + ($size[$_] + $GrCornerAdd) / 2)) })
            $s = $(if ($mid[0] -ge $mid[1]) { $others[0] } else { $others[1] })
        }
        $third = @($others | Where-Object { $_ -ne $s })[0]
        $vals = @($o[$s] + $prof) + @($ls | ForEach-Object { $_.A[$s] } | Sort-Object -Unique) + @($o[$s] + $size[$s] + $GrCornerAdd - $prof)
        $atK = (($ls | ForEach-Object { [math]::Min($_.A[$k], $_.B[$k]) }) | Measure-Object -Minimum).Minimum - 80
        $atT = (($ls | ForEach-Object { $_.A[$third] }) | Measure-Object -Average).Average
        for ($i = 1; $i -lt $vals.Count; $i++) {
            $p = [double[]]@(0, 0, 0); $q = [double[]]@(0, 0, 0)
            $p[$k] = $atK; $q[$k] = $atK; $p[$third] = $atT; $q[$third] = $atT; $p[$s] = $vals[$i - 1]; $q[$s] = $vals[$i]
            $a = ConvertTo-GrDraw $p; $b = ConvertTo-GrDraw $q
            $sa = Get-Screen $T $a[0] $a[1] $a[2]; $sb = Get-Screen $T $b[0] $b[1] $b[2]
            if ([math]::Sqrt([math]::Pow($sb[0] - $sa[0], 2) + [math]::Pow($sb[1] - $sa[1], 2)) -lt 18) { continue }   # seen end-on in this view
            Add-DimLine $List $sa $sb (Format-Number ($vals[$i] - $vals[$i - 1])) ''
        }
    }
}

function Add-GrDividers($List, $T, $Model, $Boxes) {
    $lines = @(Get-GrDividerLines $Model)
    foreach ($l in $lines) { Add-Poly3 $List $l.Kind $T @((ConvertTo-GrDraw $l.A), (ConvertTo-GrDraw $l.B)) $false }
    foreach ($p in @(Get-GrJointPoints $Model)) {
        $d = ConvertTo-GrDraw $p
        $s = Get-Screen $T $d[0] $d[1] $d[2]
        Add-Rect $List 'joint' ($s[0] - 2.5) ($s[1] - 2.5) ($s[0] + 2.5) ($s[1] + 2.5)
    }
    if ($lines.Count) { Add-GrDividerChains $List $T $lines }
}

# Colour key, top left under the caption.
function Add-GrLegend($List, [double]$X, [double]$Y) {
    $items = @(@('profile', 'P150 frame'), @('divider', 'P350'), @('dividerMilled', "P350 gefr$([char]0x00E4)st"), @('dividerMoved', 'moved'))
    foreach ($it in $items) {
        Add-Line $List $it[0] $X $Y ($X + 22) $Y
        Add-Text $List 'sub' ($X + 27) $Y $it[1] 0 0 'left'
        $X += 40 + 7 * $it[1].Length
    }
    $X += 6
    Add-Rect $List 'corner' ($X - 2.5) ($Y - 2.5) ($X + 2.5) ($Y + 2.5); Add-Text $List 'sub' ($X + 7) $Y 'corner' 0 0 'left'; $X += 56
    Add-Rect $List 'joint' ($X - 2.5) ($Y - 2.5) ($X + 2.5) ($Y + 2.5); Add-Text $List 'sub' ($X + 7) $Y 'joint' 0 0 'left'
}

function Add-GrLabels($List, $T, $Boxes, [string]$View) {
    foreach ($b in $Boxes) {
        $f = $b.F
        $z = $(if ($View -in 'Top', 'Bottom') { $b.Z1 } else { ($b.Z0 + $b.Z1) / 2 })
        $p = Get-Screen $T (($b.X0 + $b.X1) / 2) (($b.Y0 + $b.Y1) / 2) $z
        $e0 = Get-Screen $T (($b.X0 + $b.X1) / 2) $b.Y0 $z; $e1 = Get-Screen $T (($b.X0 + $b.X1) / 2) $b.Y1 $z
        $span = [math]::Sqrt([math]::Pow($e1[0] - $e0[0], 2) + [math]::Pow($e1[1] - $e0[1], 2))   # frame length on screen
        if ($View -in 'Left', 'Right') { $span = 999 }
        Add-Text $List 'label' $p[0] $p[1] (Get-GrBoxLabel $f)
        if ($span -lt 80) { continue }   # too narrow for the panel / divider count; it is in the table
        $extra = @()
        if ($f.Panels.Count) { $extra += "$($f.Panels.Count) pan." }
        if ($f.Dividers.Count) { $extra += "$($f.Dividers.Count) div." }
        if ($extra) { Add-Text $List 'sub' $p[0] ($p[1] + 14) ($extra -join ' / ') }
    }
}

function Get-GrAllPoints($Boxes) {
    $all = New-Object System.Collections.Generic.List[object]
    foreach ($b in $Boxes) { foreach ($p in (Get-GrCornersOf $b)) { $all.Add($p) } }
    , $all.ToArray()
}

function Add-GrIsoView($List, $Model, [double[]]$Rect) {
    $boxes = Get-GrBoxes $Model
    $L = $Model.Length; $W = $Model.OuterWidth; $H = $Model.OuterHeight
    $off = [math]::Max($W, $L) * 0.07
    $all = Get-GrAllPoints $boxes
    $pts = @($all) + @(, @(($W + $off * 1.4), 0, 0)) + @(, @(0, ($L + $off * 1.4), 0)) + @(, @((-$off), $L, $H))
    $T = Get-Fit 'Isometric' $pts $Rect @(30, 34, 30, 30)
    Add-GrFrames $List $T $boxes
    Add-GrDividers $List $T $Model $boxes
    Add-GrLabels $List $T $boxes 'Isometric'
    Add-IsoDim $List $T @(0, $L, 0) @($W, $L, 0) @(0, $off, 0) (Format-Number $W) 'width'
    Add-IsoDim $List $T @($W, 0, 0) @($W, $L, 0) @($off, 0, 0) (Format-Number $L) 'length'
    Add-IsoDim $List $T @(0, $L, 0) @(0, $L, $H) @((-$off * 0.55), 0, 0) (Format-Number $H) ''
}

function Add-GrOrthoView($List, $Model, [string]$View, [double[]]$Rect) {
    $boxes = Get-GrBoxes $Model
    $T = Get-Fit $View (Get-GrAllPoints $boxes) $Rect @(64, 34, 24, 70)
    Add-GrFrames $List $T $boxes
    Add-GrDividers $List $T $Model $boxes
    Add-GrLabels $List $T $boxes $View
    # Bounding box on screen for the dimension lines.
    $minX = [double]::MaxValue; $maxX = [double]::MinValue; $minY = [double]::MaxValue; $maxY = [double]::MinValue
    foreach ($p in (Get-GrAllPoints $boxes)) {
        $s = Get-Screen $T $p[0] $p[1] $p[2]
        $minX = [math]::Min($minX, $s[0]); $maxX = [math]::Max($maxX, $s[0]); $minY = [math]::Min($minY, $s[1]); $maxY = [math]::Max($maxY, $s[1])
    }
    $alongLength = $View -in 'Front', 'Back', 'Top', 'Bottom'
    if ($alongLength) {
        # Chain of frame lengths, then the overall length below it.
        foreach ($b in ($boxes | Sort-Object Y0)) {
            if ($b.Z0 -gt 0 -and $View -notin 'Top', 'Bottom') { continue }
            $a = Get-Screen $T $b.X0 $b.Y0 0; $c = Get-Screen $T $b.X0 $b.Y1 0
            if ($a[0] -gt $c[0]) { $t = $a; $a = $c; $c = $t }
            Add-DimLine $List @($a[0], ($maxY + 22)) @($c[0], ($maxY + 22)) (Format-Number ($b.Y1 - $b.Y0)) ''
        }
        Add-Line $List 'ext' $minX ($maxY + 4) $minX ($maxY + 62)
        Add-Line $List 'ext' $maxX ($maxY + 4) $maxX ($maxY + 62)
        Add-DimLine $List @($minX, ($maxY + 56)) @($maxX, ($maxY + 56)) (Format-Number $Model.Length) ''
    } else {
        Add-Line $List 'ext' $minX ($maxY + 4) $minX ($maxY + 40)
        Add-Line $List 'ext' $maxX ($maxY + 4) $maxX ($maxY + 40)
        Add-DimLine $List @($minX, ($maxY + 34)) @($maxX, ($maxY + 34)) (Format-Number $Model.OuterWidth) ''
    }
    $vSpan = $(if ($View -in 'Top', 'Bottom') { $Model.OuterWidth } else { $Model.OuterHeight })
    Add-Line $List 'ext' ($minX - 4) $minY ($minX - 40) $minY
    Add-Line $List 'ext' ($minX - 4) $maxY ($minX - 40) $maxY
    Add-DimLine $List @(($minX - 34), $maxY) @(($minX - 34), $minY) (Format-Number $vSpan) ''
}

# Text cut to roughly fit a width in px (average character width ~ 0.55 x font size).
function Get-FitText([string]$Text, [double]$Width, [double]$FontPx) {
    $max = [int][math]::Floor($Width / (0.55 * $FontPx))
    if ($Text.Length -le $max -or $max -lt 4) { return $Text }
    $Text.Substring(0, $max - 1) + [char]0x2026
}

function Get-GrTableHeight($Rows, [double]$RowH) { 22 + $RowH * (@($Rows).Count + 1) }

function Add-GrTable($List, [string]$Title, $Columns, $Rows, [double]$X, [double]$Y, [double]$W, [double]$RowH, [double]$FontPx) {
    Add-Text $List 'bomTitle' $X ($Y + 9) $Title 0 0 'left'
    $top = $Y + 22; $rows = @($Rows)
    $bottom = $top + $RowH * ($rows.Count + 1)
    Add-Rect $List 'table' $X $top ($X + $W) $bottom
    for ($k = 1; $k -le $rows.Count; $k++) { Add-Line $List 'table' $X ($top + $RowH * $k) ($X + $W) ($top + $RowH * $k) }
    $cx = $X
    for ($c = 0; $c -lt $Columns.Count; $c++) {
        $cw = $W * $Columns[$c][1]
        if ($c -gt 0) { Add-Line $List 'table' $cx $top $cx $bottom }
        Add-Text $List 'bomHead' ($cx + 5) ($top + $RowH / 2) $Columns[$c][0] 0 0 'left'
        for ($k = 0; $k -lt $rows.Count; $k++) {
            Add-Text $List 'bom' ($cx + 5) ($top + $RowH * ($k + 1.5)) (Get-FitText ([string]$rows[$k].Cells[$c]) ($cw - 10) $FontPx) 0 0 'left'
        }
        $cx += $cw
    }
}

$GrFrameColumns = @(@('Box', 0.14), @('L x W x H (profiles)', 0.40), @('Attached', 0.46))
$GrPartColumns  = @(@('Item', 0.24), @('Qty', 0.08), @('Lengths / sizes (mm)', 0.68))

function Get-GrWindowShapes($Model, [string]$View, [double]$W, [double]$H) {
    $list = New-Object System.Collections.Generic.List[object]
    if (-not $Model) {
        Add-Text $list 'empty' ($W / 2) ($H / 2) 'GR view: load a GR export, or read a GR assembly from Inventor'
        return , $list.ToArray()
    }
    $tables = Get-GrTables $Model
    $rowH = 17
    $tabH = [math]::Max((Get-GrTableHeight $tables.Frames $rowH), (Get-GrTableHeight $tables.Parts $rowH))
    $viewH = [math]::Max(140, $H - $tabH - 16)
    $rect = [double[]]@(0, 0, $W, $viewH)
    if ($View -eq 'Isometric') { Add-GrIsoView $list $Model $rect } else { Add-GrOrthoView $list $Model $View $rect }
    Add-Text $list 'caption' 10 14 "GR $($View.ToUpperInvariant()) - $($Model.Name)" 0 0 'left'
    Add-Text $list 'sub' 10 32 $(if ($Model.PSObject.Properties.Name -contains 'RealMap' -and $Model.RealMap) { "Real part positions ($($Model.Source))" } else { "Layout inferred from the constraints ($($Model.Source)) - dividers schematic" }) 0 0 'left'
    Add-GrLegend $list 14 50
    $y = $H - $tabH - 6
    $leftW = [math]::Floor(($W - 30) * 0.4)
    Add-GrTable $list 'Frames' $GrFrameColumns $tables.Frames 10 $y $leftW $rowH 11
    Add-GrTable $list 'Cut list and parts' $GrPartColumns $tables.Parts (20 + $leftW) $y ($W - 30 - $leftW) $rowH 11
    Add-Axis $list $View ($W - 60) 70
    , $list.ToArray()
}

function Get-GrSheetShapes($Model) {
    $list = New-Object System.Collections.Generic.List[object]
    Add-Rect $list 'frame' 15 15 1385 975
    if (-not $Model) { Add-Text $list 'empty' 700 495 'No GR model loaded'; return , $list.ToArray() }
    Add-GrIsoView $list $Model ([double[]]@(30, 30, 640, 420))
    Add-Text $list 'caption' 40 46 "ISOMETRIC - $($Model.Name)" 0 0 'left'
    Add-Axis $list 'Isometric' 70 420
    Add-GrOrthoView $list $Model 'Front' ([double[]]@(690, 30, 680, 230))
    Add-Text $list 'caption' 700 46 'FRONT' 0 0 'left'
    Add-GrOrthoView $list $Model 'Top' ([double[]]@(690, 270, 680, 230))
    Add-Text $list 'caption' 700 286 'TOP' 0 0 'left'
    $tables = Get-GrTables $Model
    $y = 520
    Add-GrTable $list 'Frames' $GrFrameColumns $tables.Frames 30 $y 520 22 12
    Add-GrTable $list 'Cut list and parts' $GrPartColumns $tables.Parts 570 $y 800 22 12
    $ny = [math]::Max($y + (Get-GrTableHeight $tables.Frames 22), $y + (Get-GrTableHeight $tables.Parts 22)) + 24
    foreach ($line in (Get-GrNotes $Model)) { Add-Text $list 'note' 30 $ny $line 0 0 'left'; $ny += 20 }
    , $list.ToArray()
}
