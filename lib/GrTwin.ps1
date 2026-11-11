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

function Add-GrLabels($List, $T, $Boxes, [string]$View) {
    foreach ($b in $Boxes) {
        $f = $b.F
        $z = $(if ($View -in 'Top', 'Bottom') { $b.Z1 } else { ($b.Z0 + $b.Z1) / 2 })
        $p = Get-Screen $T (($b.X0 + $b.X1) / 2) (($b.Y0 + $b.Y1) / 2) $z
        $e0 = Get-Screen $T (($b.X0 + $b.X1) / 2) $b.Y0 $z; $e1 = Get-Screen $T (($b.X0 + $b.X1) / 2) $b.Y1 $z
        $span = [math]::Sqrt([math]::Pow($e1[0] - $e0[0], 2) + [math]::Pow($e1[1] - $e0[1], 2))   # frame length on screen
        if ($View -in 'Left', 'Right') { $span = 999 }
        Add-Text $List 'label' $p[0] $p[1] "F$($f.No)"
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
    Add-GrLabels $List $T $boxes 'Isometric'
    Add-IsoDim $List $T @(0, $L, 0) @($W, $L, 0) @(0, $off, 0) (Format-Number $W) 'width'
    Add-IsoDim $List $T @($W, 0, 0) @($W, $L, 0) @($off, 0, 0) (Format-Number $L) 'length'
    Add-IsoDim $List $T @(0, $L, 0) @(0, $L, $H) @((-$off * 0.55), 0, 0) (Format-Number $H) ''
}

function Add-GrOrthoView($List, $Model, [string]$View, [double[]]$Rect) {
    $boxes = Get-GrBoxes $Model
    $T = Get-Fit $View (Get-GrAllPoints $boxes) $Rect @(64, 34, 24, 70)
    Add-GrFrames $List $T $boxes
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

$GrFrameColumns = @(@('Frame', 0.14), @('L x W x H (profiles)', 0.40), @('Attached', 0.46))
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
    Add-Text $list 'sub' 10 32 "Layout inferred from the constraints ($($Model.Source))" 0 0 'left'
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
