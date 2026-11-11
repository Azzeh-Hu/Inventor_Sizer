# Vector drawings of the panel ("digital twin"): isometric and orthographic views, a bill of
# materials and a reference axis.
#
# A drawing is built as a list of shapes in view coordinates:
#   Get-WindowShapes  one view + bill of materials + axis, sized to the window
#   Get-SheetShapes   drawing sheet for SVG: isometric top left, front and top views in the
#                     middle, bill of materials and hole notes
# Invoke-TwinPaint draws shapes in the window (GDI+); Export-TwinSvg writes them as SVG.
#
# Model axes: x = Width, y = Length, z = Height (mm).

$TwinCos   = [math]::Cos([math]::PI / 6)
$TwinSin   = 0.5
$TwinGdi   = @{}   # pens, brushes and fonts, created on first paint
$TwinViews = @('Isometric', 'Front', 'Back', 'Left', 'Right', 'Top', 'Bottom')
# The side each orthographic view looks from (used for the axis icon).
$TwinViewer = @{ Front = @(1, 0, 0); Back = @(-1, 0, 0); Right = @(0, 1, 0); Left = @(0, -1, 0); Top = @(0, 0, 1); Bottom = @(0, 0, -1) }

# Projection to unscaled view coordinates (u to the right, v down; right-handed views).
function Get-Proj([string]$View, [double]$x, [double]$y, [double]$z) {
    switch ($View) {
        'Front'  { return , @($y, -$z) }
        'Back'   { return , @(-$y, -$z) }
        'Right'  { return , @(-$x, -$z) }
        'Left'   { return , @($x, -$z) }
        'Top'    { return , @($y, $x) }
        'Bottom' { return , @($y, -$x) }
        default  { return , @((($x - $y) * $TwinCos), (($x + $y) * $TwinSin - $z)) }
    }
}

function Get-Screen($T, [double]$x, [double]$y, [double]$z) {
    $p = Get-Proj $T.View $x $y $z
    , @(($p[0] * $T.S + $T.X), ($p[1] * $T.S + $T.Y))
}

# Scale and offset that fit model points (plus margins in px) into a rectangle.
function Get-Fit([string]$View, $Points3d, [double[]]$Rect, [double[]]$Margin) {
    $minU = [double]::MaxValue; $maxU = [double]::MinValue; $minV = [double]::MaxValue; $maxV = [double]::MinValue
    foreach ($c in $Points3d) {
        $q = Get-Proj $View $c[0] $c[1] $c[2]
        $minU = [math]::Min($minU, $q[0]); $maxU = [math]::Max($maxU, $q[0])
        $minV = [math]::Min($minV, $q[1]); $maxV = [math]::Max($maxV, $q[1])
    }
    $w = $Rect[2] - $Margin[0] - $Margin[2]; $h = $Rect[3] - $Margin[1] - $Margin[3]
    $s = [math]::Max([math]::Min($w / [math]::Max($maxU - $minU, 1e-6), $h / [math]::Max($maxV - $minV, 1e-6)), 1e-6)
    @{
        View = $View; S = $s
        X = $Rect[0] + $Margin[0] + ($w - ($maxU - $minU) * $s) / 2 - $minU * $s
        Y = $Rect[1] + $Margin[1] + ($h - ($maxV - $minV) * $s) / 2 - $minV * $s
    }
}

# ------------------------------------------------------------------ shape helpers

function Add-Shape($List, [string]$Kind, [double[]]$Points, [bool]$Closed) {
    $List.Add([pscustomobject]@{ Kind = $Kind; Points = $Points; Closed = $Closed })
}

function Add-Poly3($List, [string]$Kind, $T, $Points3d, [bool]$Closed) {
    $flat = New-Object System.Collections.Generic.List[double]
    foreach ($p in $Points3d) {
        $v = Get-Screen $T $p[0] $p[1] $p[2]
        $flat.Add($v[0]); $flat.Add($v[1])
    }
    Add-Shape $List $Kind $flat.ToArray() $Closed
}

function Add-Text($List, [string]$Kind, [double]$X, [double]$Y, [string]$Text, [double]$Angle = 0, [double]$Dy = 0, [string]$Align = 'center') {
    $List.Add([pscustomobject]@{ Kind = $Kind; Points = $null; X = $X; Y = $Y; Angle = $Angle; Dy = $Dy; Text = $Text; Align = $Align })
}

function Add-Line($List, [string]$Kind, [double]$X1, [double]$Y1, [double]$X2, [double]$Y2) {
    Add-Shape $List $Kind @($X1, $Y1, $X2, $Y2) $false
}

function Add-Circle($List, [string]$Kind, [double]$Cx, [double]$Cy, [double]$R) {
    $pts = for ($i = 0; $i -lt 24; $i++) {
        $a = $i / 24 * 2 * [math]::PI
        $Cx + [math]::Cos($a) * $R; $Cy + [math]::Sin($a) * $R
    }
    Add-Shape $List $Kind ([double[]]$pts) $true
}

function Add-Rect($List, [string]$Kind, [double]$X1, [double]$Y1, [double]$X2, [double]$Y2) {
    Add-Shape $List $Kind @($X1, $Y1, $X2, $Y1, $X2, $Y2, $X1, $Y2) $true
}

# Dimension line between screen points A and B, with arrows and a label (+ optional second line).
function Add-DimLine($List, [double[]]$A, [double[]]$B, [string]$Label, [string]$Sub) {
    Add-Line $List 'dim' $A[0] $A[1] $B[0] $B[1]
    $len = [math]::Max([math]::Sqrt([math]::Pow($B[0] - $A[0], 2) + [math]::Pow($B[1] - $A[1], 2)), 1)
    $ux = ($B[0] - $A[0]) / $len; $uy = ($B[1] - $A[1]) / $len
    foreach ($end in @(@($A[0], $A[1], 1), @($B[0], $B[1], -1))) {
        $bx = $end[0] + $ux * 11 * $end[2]; $by = $end[1] + $uy * 11 * $end[2]
        Add-Shape $List 'arrow' @($end[0], $end[1], ($bx - $uy * 3.2), ($by + $ux * 3.2), ($bx + $uy * 3.2), ($by - $ux * 3.2)) $true
    }
    $angle = [math]::Atan2($B[1] - $A[1], $B[0] - $A[0]) * 180 / [math]::PI
    if ($angle -gt 90) { $angle -= 180 }
    if ($angle -lt -90) { $angle += 180 }
    $mx = ($A[0] + $B[0]) / 2; $my = ($A[1] + $B[1]) / 2
    Add-Text $List 'label' $mx $my $Label $angle -9
    if ($Sub) { Add-Text $List 'sub' $mx $my $Sub $angle 9 }
}

function Get-HoleText($WallLayout, [double]$D) {
    if ($null -eq $WallLayout) { return '' }
    $n = [int][math]::Round($WallLayout.Holes)
    if ($n -eq 0) { return 'no holes' }
    "$n $([char]0x00D7) $([char]0x00D8)$(Format-Number $D)"
}

# Model point on a wall: $Along = 'y' for the length walls (x fixed), 'x' for the width walls (y fixed).
function Get-WallPoint([string]$Along, [double]$Fixed, [double]$Pos, [double]$Z) {
    if ($Along -eq 'y') { , @($Fixed, $Pos, $Z) } else { , @($Pos, $Fixed, $Z) }
}

function Get-HoleKind($WallLayout, [bool]$Hidden) {
    if ($Hidden) { 'hiddenHole' } elseif (@($WallLayout.Errors).Count -gt 0) { 'holeBad' } else { 'hole' }
}

# ------------------------------------------------------------------ door hardware

# Circle of radius R around a model point, in the plane across the given axis (x, y or z).
function Add-Ring3($List, [string]$Kind, $T, [double[]]$C, [string]$Axis, [double]$R) {
    $ring = for ($i = 0; $i -lt 24; $i++) {
        $a = $i / 24 * 2 * [math]::PI; $p = [math]::Cos($a) * $R; $q = [math]::Sin($a) * $R
        switch ($Axis) {
            'x' { , @($C[0], ($C[1] + $p), ($C[2] + $q)) }
            'y' { , @(($C[0] + $p), $C[1], ($C[2] + $q)) }
            default { , @(($C[0] + $p), ($C[1] + $q), $C[2]) }
        }
    }
    Add-Poly3 $List $Kind $T $ring $true
}

# Which hardware holes a view shows, and whether they are seen directly (else dashed).
# Face holes show in the isometric and plan views; wall holes in the elevation facing that wall.
function Get-HardwareVisibility($Hole, [string]$View) {
    switch ($View) {
        'Isometric' { switch ($Hole.Wall) { 'face' { return $Hole.Side -eq 'top' } 'length' { return $Hole.Side -eq 'xW' } default { return $Hole.Side -eq 'yL' } } }
        'Top'    { if ($Hole.Wall -eq 'face') { return $Hole.Side -eq 'top' } }
        'Bottom' { if ($Hole.Wall -eq 'face') { return $Hole.Side -eq 'bottom' } }
        'Front'  { if ($Hole.Wall -eq 'length') { return $Hole.Side -eq 'xW' } }
        'Back'   { if ($Hole.Wall -eq 'length') { return $Hole.Side -eq 'x0' } }
        'Right'  { if ($Hole.Wall -eq 'width') { return $Hole.Side -eq 'yL' } }
        'Left'   { if ($Hole.Wall -eq 'width') { return $Hole.Side -eq 'y0' } }
    }
    $null   # not shown in this view
}

# Hinge / handle holes. Holes on the far face are dashed but always drawn in the isometric and
# plan views (the hardware is usually on the outside face, which these views look away from);
# in the elevations the far wall's holes only appear with hidden edges on.
function Add-HardwareHoles($List, $T, $V, [string]$View, [bool]$ShowHidden) {
    foreach ($h in @($V.HW)) {
        $seen = Get-HardwareVisibility $h $View
        if ($null -eq $seen) { continue }
        if (-not $seen -and $View -notin 'Isometric', 'Top', 'Bottom' -and -not $ShowHidden) { continue }
        $kind = $(if ($h.Bad) { 'hwBad' } elseif ($seen) { 'hw' } else { 'hwHidden' })
        $r = [math]::Max($h.Dia / 2, 4.5 / $T.S)   # at least 4.5 px, so small holes stay visible on a large panel
        $c = [double[]]@($h.X, $h.Y, $h.Z)
        Add-Ring3 $List $kind $T $c $h.Axis $r
        if ($seen) {
            $k = 1.6 * $r
            $d = switch ($h.Axis) { 'x' { @(@(0, $k, 0), @(0, 0, $k)) } 'y' { @(@($k, 0, 0), @(0, 0, $k)) } default { @(@($k, 0, 0), @(0, $k, 0)) } }
            foreach ($e in $d) {
                Add-Poly3 $List 'center' $T @(@(($c[0] - $e[0]), ($c[1] - $e[1]), ($c[2] - $e[2])), @(($c[0] + $e[0]), ($c[1] + $e[1]), ($c[2] + $e[2]))) $false
            }
        }
    }
}

# ------------------------------------------------------------------ isometric view

function Add-IsoHoles($List, $T, $WallLayout, [string]$Along, [double]$Fixed, [double]$Z, [double]$R, [bool]$Hidden) {
    if ($null -eq $WallLayout) { return }
    $kind = Get-HoleKind $WallLayout $Hidden
    foreach ($pos in @($WallLayout.Positions)) {
        $ring = for ($i = 0; $i -lt 24; $i++) {
            $ang = $i / 24 * 2 * [math]::PI
            Get-WallPoint $Along $Fixed ($pos + [math]::Cos($ang) * $R) ($Z + [math]::Sin($ang) * $R)
        }
        Add-Poly3 $List $kind $T $ring $true
        if (-not $Hidden) {
            Add-Poly3 $List 'center' $T @((Get-WallPoint $Along $Fixed ($pos - 1.8 * $R) $Z), (Get-WallPoint $Along $Fixed ($pos + 1.8 * $R) $Z)) $false
            Add-Poly3 $List 'center' $T @((Get-WallPoint $Along $Fixed $pos ($Z - 1.8 * $R)), (Get-WallPoint $Along $Fixed $pos ($Z + 1.8 * $R))) $false
        }
    }
}

function Get-Offset([double[]]$P, [double[]]$O, [double]$K) {
    , @(($P[0] + $O[0] * $K), ($P[1] + $O[1] * $K), ($P[2] + $O[2] * $K))
}

function Add-IsoDim($List, $T, [double[]]$A, [double[]]$B, [double[]]$O, [string]$Label, [string]$Sub) {
    Add-Poly3 $List 'ext' $T @((Get-Offset $A $O 0.12), (Get-Offset $A $O 1.08)) $false
    Add-Poly3 $List 'ext' $T @((Get-Offset $B $O 0.12), (Get-Offset $B $O 1.08)) $false
    $pa = Get-Offset $A $O 1; $pb = Get-Offset $B $O 1
    Add-DimLine $List (Get-Screen $T $pa[0] $pa[1] $pa[2]) (Get-Screen $T $pb[0] $pb[1] $pb[2]) $Label $Sub
}

function Add-IsoView($List, $V, $Layout, [double[]]$Rect, [bool]$ShowHidden) {
    $W = $V.W; $L = $V.L; $H = $V.H
    $off = [math]::Max($W, $L) * 0.11
    $pts = @(@(0, 0, 0), @($W, 0, 0), @(0, $L, 0), @($W, $L, 0), @(0, 0, $H), @($W, 0, $H), @(0, $L, $H), @($W, $L, $H),
             @(($W + $off * 1.4), 0, 0), @(($W + $off * 1.4), $L, 0), @(0, ($L + $off * 1.4), 0), @($W, ($L + $off * 1.4), 0),
             @((-$off * 0.8), $L, $H))
    $T = Get-Fit 'Isometric' $pts $Rect @(30, 34, 30, 30)
    $s = $T.S

    # Faces (filled with the background, so they hide what is behind them)
    Add-Poly3 $List 'face' $T @(@($W, 0, 0), @($W, $L, 0), @($W, $L, $H), @($W, 0, $H)) $true
    Add-Poly3 $List 'face' $T @(@(0, $L, 0), @($W, $L, 0), @($W, $L, $H), @(0, $L, $H)) $true
    Add-Poly3 $List 'face' $T @(@(0, 0, $H), @($W, 0, $H), @($W, $L, $H), @(0, $L, $H)) $true
    # -In panel outline on top, inset by thickness + clearance (drawn at least a few px in)
    $i = [math]::Max($V.T + $V.C, 6 / $s)
    Add-Poly3 $List 'inner' $T @(@($i, $i, $H), @(($W - $i), $i, $H), @(($W - $i), ($L - $i), $H), @($i, ($L - $i), $H)) $true
    if ($Layout -and $Layout.Split -ge 2) {   # seam between the 2 halves of the inner panel
        Add-Poly3 $List 'inner' $T @(@(($W / 2), $i, $H), @(($W / 2), ($L - $i), $H)) $false
    }

    $r = [math]::Max($V.D / 2, 4 / $s)
    $lenWall = $null; $widWall = $null
    if ($Layout) { $lenWall = $Layout.Length; $widWall = $Layout.Width }
    if ($ShowHidden) {   # back edges and back-wall holes, dashed, on top of the faces
        Add-Poly3 $List 'hidden' $T @(@(0, 0, 0), @($W, 0, 0)) $false
        Add-Poly3 $List 'hidden' $T @(@(0, 0, 0), @(0, $L, 0)) $false
        Add-Poly3 $List 'hidden' $T @(@(0, 0, 0), @(0, 0, $H)) $false
        Add-IsoHoles $List $T $lenWall 'y' 0 $V.Z $r $true
        Add-IsoHoles $List $T $widWall 'x' 0 $V.Z $r $true
    }
    Add-IsoHoles $List $T $lenWall 'y' $W $V.Z $r $false
    Add-IsoHoles $List $T $widWall 'x' $L $V.Z $r $false
    Add-HardwareHoles $List $T $V 'Isometric' $ShowHidden

    Add-IsoDim $List $T @(0, $L, 0) @($W, $L, 0) @(0, $off, 0) (Format-Number $W) (Get-HoleText $widWall $V.D)
    Add-IsoDim $List $T @($W, 0, 0) @($W, $L, 0) @($off, 0, 0) (Format-Number $L) (Get-HoleText $lenWall $V.D)
    Add-IsoDim $List $T @(0, $L, 0) @(0, $L, $H) @((-$off * 0.55), 0, 0) (Format-Number $H) ''
}

# ------------------------------------------------------------------ orthographic views

function Add-OrthoView($List, $V, $Layout, [string]$View, [double[]]$Rect, [bool]$ShowHidden) {
    $W = $V.W; $L = $V.L; $H = $V.H; $thick = $V.T
    $corners = @(@(0, 0, 0), @($W, 0, 0), @(0, $L, 0), @($W, $L, 0), @(0, 0, $H), @($W, 0, $H), @(0, $L, $H), @($W, $L, $H))
    $T = Get-Fit $View $corners $Rect @(64, 34, 24, 62)
    $s = $T.S
    $minX = [double]::MaxValue; $maxX = [double]::MinValue; $minY = [double]::MaxValue; $maxY = [double]::MinValue
    foreach ($c in $corners) {
        $q = Get-Screen $T $c[0] $c[1] $c[2]
        $minX = [math]::Min($minX, $q[0]); $maxX = [math]::Max($maxX, $q[0])
        $minY = [math]::Min($minY, $q[1]); $maxY = [math]::Max($maxY, $q[1])
    }
    Add-Rect $List 'face' $minX $minY $maxX $maxY

    $lenWall = $null; $widWall = $null
    if ($Layout) { $lenWall = $Layout.Length; $widWall = $Layout.Width }
    $r = [math]::Max($V.D / 2, 3.5 / $s)
    $plan = $View -in 'Top', 'Bottom'

    if (-not $plan) {
        # Elevation: the wall facing the viewer shows its holes as circles.
        $wall = $null; $along = 'y'; $fixed = 0
        switch ($View) {
            'Front' { $wall = $lenWall; $along = 'y'; $fixed = $W }
            'Back'  { $wall = $lenWall; $along = 'y'; $fixed = 0 }
            'Right' { $wall = $widWall; $along = 'x'; $fixed = $L }
            'Left'  { $wall = $widWall; $along = 'x'; $fixed = 0 }
        }
        if ($ShowHidden) {   # floor and side-wall thickness
            $tv = [math]::Max($thick * $s, 3)
            Add-Line $List 'hidden' $minX ($maxY - $tv) $maxX ($maxY - $tv)
            Add-Line $List 'hidden' ($minX + $tv) $minY ($minX + $tv) $maxY
            Add-Line $List 'hidden' ($maxX - $tv) $minY ($maxX - $tv) $maxY
        }
        if ($wall) {
            $kind = Get-HoleKind $wall $false
            foreach ($pos in @($wall.Positions)) {
                $p = Get-WallPoint $along $fixed $pos $V.Z
                $c = Get-Screen $T $p[0] $p[1] $p[2]
                Add-Circle $List $kind $c[0] $c[1] ($r * $s)
                Add-Line $List 'center' ($c[0] - 1.8 * $r * $s) $c[1] ($c[0] + 1.8 * $r * $s) $c[1]
                Add-Line $List 'center' $c[0] ($c[1] - 1.8 * $r * $s) $c[0] ($c[1] + 1.8 * $r * $s)
            }
        }
    } else {
        # Plan: top shows the -In panel; holes sit in the walls, so they show as centre lines
        # across the wall (and as hidden lines when hidden edges are on).
        if ($View -eq 'Top') {
            $i = [math]::Max($thick + $V.C, 5 / $s)
            Add-Poly3 $List 'edge' $T @(@($i, $i, $H), @(($W - $i), $i, $H), @(($W - $i), ($L - $i), $H), @($i, ($L - $i), $H)) $true
            if ($Layout -and $Layout.Split -ge 2) {
                Add-Poly3 $List 'edge' $T @(@(($W / 2), $i, $H), @(($W / 2), ($L - $i), $H)) $false
            }
        }
        $tw = [math]::Max($thick, 4 / $s); $ext = 8 / $s
        foreach ($set in @(@($lenWall, 'y', 0, 1), @($lenWall, 'y', $W, -1), @($widWall, 'x', 0, 1), @($widWall, 'x', $L, -1))) {
            $wall = $set[0]; $along = $set[1]; $edge = $set[2]; $dir = $set[3]
            if ($null -eq $wall) { continue }
            foreach ($pos in @($wall.Positions)) {
                $out = $edge - $dir * $ext; $in = $edge + $dir * ($tw + $ext)
                if ($along -eq 'y') { Add-Poly3 $List 'center' $T @(@($out, $pos, $H), @($in, $pos, $H)) $false }
                else { Add-Poly3 $List 'center' $T @(@($pos, $out, $H), @($pos, $in, $H)) $false }
                if ($ShowHidden) {
                    foreach ($side in @(-$r, $r)) {
                        $kind = Get-HoleKind $wall $true
                        if ($along -eq 'y') { Add-Poly3 $List $kind $T @(@($edge, ($pos + $side), $H), @(($edge + $dir * $tw), ($pos + $side), $H)) $false }
                        else { Add-Poly3 $List $kind $T @(@(($pos + $side), $edge, $H), @(($pos + $side), ($edge + $dir * $tw), $H)) $false }
                    }
                }
            }
        }
    }

    Add-HardwareHoles $List $T $V $View $ShowHidden

    # Overall dimensions: horizontal below, vertical on the left
    $hSpan = $(if ($View -in 'Left', 'Right') { $W } else { $L })
    $vSpan = $(if ($plan) { $W } else { $H })
    $hSub = $(if ($View -in 'Left', 'Right') { Get-HoleText $widWall $V.D } else { Get-HoleText $lenWall $V.D })
    $vSub = $(if ($plan) { Get-HoleText $widWall $V.D } else { '' })
    Add-Line $List 'ext' $minX ($maxY + 4) $minX ($maxY + 40)
    Add-Line $List 'ext' $maxX ($maxY + 4) $maxX ($maxY + 40)
    Add-DimLine $List @($minX, ($maxY + 34)) @($maxX, ($maxY + 34)) (Format-Number $hSpan) $hSub
    Add-Line $List 'ext' ($minX - 4) $minY ($minX - 40) $minY
    Add-Line $List 'ext' ($minX - 4) $maxY ($minX - 40) $maxY
    Add-DimLine $List @(($minX - 34), $maxY) @(($minX - 34), $minY) (Format-Number $vSpan) $vSub
}

# ------------------------------------------------------------------ axis, bill of materials, notes

function Add-Axis($List, [string]$View, [double]$Cx, [double]$Cy, [double]$Len = 34) {
    $o = Get-Proj $View 0 0 0
    foreach ($axis in @(@('X', 1, 0, 0), @('Y', 0, 1, 0), @('Z', 0, 0, 1))) {
        $name = $axis[0]
        $p = Get-Proj $View $axis[1] $axis[2] $axis[3]
        $dx = $p[0] - $o[0]; $dy = $p[1] - $o[1]
        $n = [math]::Sqrt($dx * $dx + $dy * $dy)
        if ($n -lt 1e-6) {
            # Axis points straight at or away from the viewer: dot = towards, cross = away.
            $viewer = $TwinViewer[$View]
            $toward = ($axis[1] * $viewer[0] + $axis[2] * $viewer[1] + $axis[3] * $viewer[2]) -gt 0
            Add-Circle $List 'axisRing' $Cx $Cy 7
            if ($toward) { Add-Circle $List "axis$name" $Cx $Cy 2.5 }
            else {
                Add-Line $List "axis$name" ($Cx - 4.5) ($Cy - 4.5) ($Cx + 4.5) ($Cy + 4.5)
                Add-Line $List "axis$name" ($Cx - 4.5) ($Cy + 4.5) ($Cx + 4.5) ($Cy - 4.5)
            }
            Add-Text $List 'axisLabel' ($Cx - 13) ($Cy + 12) $name
            continue
        }
        $ux = $dx / $n; $uy = $dy / $n
        $ex = $Cx + $ux * $Len; $ey = $Cy + $uy * $Len
        Add-Line $List "axis$name" $Cx $Cy $ex $ey
        Add-Shape $List "axis$name" @($ex, $ey, ($ex - $ux * 8 - $uy * 3.5), ($ey - $uy * 8 + $ux * 3.5), ($ex - $ux * 8 + $uy * 3.5), ($ey - $uy * 8 - $ux * 3.5)) $true
        Add-Text $List 'axisLabel' ($ex + $ux * 10) ($ey + $uy * 10) $name
    }
}

function Get-BomHeight($Bom, [double]$RowH) {
    if (-not $Bom) { return 0 }
    $h = 22 + $RowH * (@($Bom.Rows).Count + 1)
    if ($Bom.Note) { $h += 20 }
    $h
}

function Add-Bom($List, $Bom, [double]$X, [double]$Y, [double]$W, [double]$RowH) {
    if (-not $Bom) { return }
    $cols = @(@('Pos', 0.07), @('Part number', 0.27), @('Description', 0.30), @('Qty', 0.08), @('Size (mm)', 0.28))
    Add-Text $List 'bomTitle' $X ($Y + 9) $Bom.Title 0 0 'left'
    $top = $Y + 22
    $rows = @($Bom.Rows)
    $bottom = $top + $RowH * ($rows.Count + 1)
    Add-Rect $List 'table' $X $top ($X + $W) $bottom
    for ($k = 1; $k -le $rows.Count; $k++) { Add-Line $List 'table' $X ($top + $RowH * $k) ($X + $W) ($top + $RowH * $k) }
    $cx = $X
    for ($c = 0; $c -lt $cols.Count; $c++) {
        if ($c -gt 0) { Add-Line $List 'table' $cx $top $cx $bottom }
        Add-Text $List 'bomHead' ($cx + 5) ($top + $RowH / 2) $cols[$c][0] 0 0 'left'
        $cx += $W * $cols[$c][1]
    }
    for ($k = 0; $k -lt $rows.Count; $k++) {
        $row = $rows[$k]
        $cells = @($row.Pos, $row.Part, $row.Desc, $row.Qty, $row.Size)
        $cx = $X
        for ($c = 0; $c -lt $cols.Count; $c++) {
            Add-Text $List 'bom' ($cx + 5) ($top + $RowH * ($k + 1.5)) ([string]$cells[$c]) 0 0 'left'
            $cx += $W * $cols[$c][1]
        }
    }
    if ($Bom.Note) { Add-Text $List 'note' $X ($bottom + 11) $Bom.Note 0 0 'left' }
}

function Get-HoleNotes($Layout, $V) {
    $dia = [char]0x00D8
    $lines = @("Holes $dia$(Format-Number $V.D) mm, centre height $(Format-Number $V.Z) mm from the bottom")
    if ($Layout) {
        foreach ($wall in @($Layout.Length, $Layout.Width)) {
            $n = [int][math]::Round($wall.Holes)
            $at = (@($wall.Positions) | ForEach-Object { Format-Number $_ }) -join ' / '
            $lines += $(if ($n -eq 0) { "$($wall.Wall) walls (2x): no holes" }
                        else { "$($wall.Wall) walls (2x): $n hole(s) at $at mm from the wall end" })
        }
    }
    $hw = @($V.HW)
    if ($hw.Count) {
        $face = @($hw | Where-Object { $_.Wall -eq 'face' }).Count
        $lines += "Door hardware (orange): $($hw.Count) hole(s), $face in the face, $($hw.Count - $face) in the walls; dashed = far side"
        foreach ($g in ($hw | Group-Object { Format-Number $_.Dia })) { $lines += "  $([char]0x00D8)$($g.Name) mm: $($g.Count)x" }
    }
    $lines += 'All dimensions in mm. Drawing not to scale.'
    $lines
}

# ------------------------------------------------------------------ compositions

function ConvertTo-TwinValues([hashtable]$Values, [double]$Thickness, $Hardware) {
    $d = [double]$Values['HoleDia']
    if ($d -le 0) { $d = 4.1 }
    @{ W = [double]$Values['Width']; L = [double]$Values['Length']; H = [double]$Values['Height']
       Z = [double]$Values['HoleZ']; D = $d; C = [double]$Values['Clearance']; T = $Thickness; HW = @($Hardware) }
}

function Test-TwinValues($V) { $V.W -gt 0 -and $V.L -gt 0 -and $V.H -gt 0 }

# One view in the window, with the bill of materials bottom right and the axis bottom left.
function Get-WindowShapes([hashtable]$Values, $Layout, [string]$View, [double]$W, [double]$H, [bool]$ShowHidden, $Bom, [double]$Thickness, $Hardware = @()) {
    $list = New-Object System.Collections.Generic.List[object]
    $V = ConvertTo-TwinValues $Values $Thickness $Hardware
    if (-not (Test-TwinValues $V) -or $W -lt 160 -or $H -lt 160) {
        Add-Text $list 'empty' ($W / 2) ($H / 2) 'Enter the dimensions'
        return , $list.ToArray()
    }
    $rowH = 19
    $bomH = Get-BomHeight $Bom $rowH
    $viewH = [math]::Max(120, $H - $bomH - 16)
    $rect = [double[]]@(0, 0, $W, $viewH)
    if ($View -eq 'Isometric') { Add-IsoView $list $V $Layout $rect $ShowHidden }
    else { Add-OrthoView $list $V $Layout $View $rect $ShowHidden }
    Add-Text $list 'caption' 10 14 $View.ToUpperInvariant() 0 0 'left'
    if (@($V.HW).Count) { Add-Text $list 'sub' 10 32 'Orange: door hardware holes (dashed = far side)' 0 0 'left' }
    $bomW = [math]::Min(620, $W - 110)
    Add-Bom $list $Bom ($W - $bomW - 10) ($H - $bomH - 8) $bomW $rowH
    Add-Axis $list $View 48 ($H - 48)
    , $list.ToArray()
}

# Drawing sheet for the SVG file (1400 x 990, A3 proportions).
function Get-SheetShapes([hashtable]$Values, $Layout, $Bom, [double]$Thickness, [bool]$ShowHidden, $Hardware = @()) {
    $list = New-Object System.Collections.Generic.List[object]
    $V = ConvertTo-TwinValues $Values $Thickness $Hardware
    Add-Rect $list 'frame' 15 15 1385 975
    if (-not (Test-TwinValues $V)) {
        Add-Text $list 'empty' 700 495 'Enter the dimensions'
        return , $list.ToArray()
    }
    # Isometric, centred in the top-left cell
    Add-IsoView $list $V $Layout ([double[]]@(30, 30, 640, 450)) $ShowHidden
    Add-Text $list 'caption' 40 46 'ISOMETRIC' 0 0 'left'
    Add-Axis $list 'Isometric' 70 450
    # Front and top views in the middle area (top view below the front view)
    Add-OrthoView $list $V $Layout 'Front' ([double[]]@(690, 30, 680, 300)) $true
    Add-Text $list 'caption' 700 46 'FRONT' 0 0 'left'
    Add-OrthoView $list $V $Layout 'Top' ([double[]]@(690, 340, 680, 360)) $true
    Add-Text $list 'caption' 700 356 'TOP' 0 0 'left'
    Add-Line $list 'table' 680 30 680 960
    # Hole notes bottom left, bill of materials bottom right
    $y = 530
    Add-Text $list 'bomTitle' 40 $y 'Notes' 0 0 'left'
    foreach ($line in (Get-HoleNotes $Layout $V)) { $y += 24; Add-Text $list 'note' 40 $y $line 0 0 'left' }
    $rowH = 24
    $bomH = Get-BomHeight $Bom $rowH
    Add-Bom $list $Bom 700 (960 - $bomH) 670 $rowH
    , $list.ToArray()
}

# ------------------------------------------------------------------ window (GDI+)

function Initialize-TwinGdi {
    $pens = @{
        face = @('Black', 1.3); edge = @('Black', 1); inner = @('Gray', 1, 'Dash'); hidden = @('Gray', 1, 'Dash')
        hole = @('Black', 1.1); holeBad = @('DarkRed', 1.5); hiddenHole = @('Gray', 1, 'Dash'); center = @('Gray', 0.8, 'DashDot')
        dim = @('DimGray', 1); ext = @('Silver', 1); table = @('Black', 1); frame = @('Black', 1.6)
        axisX = @('Red', 1.6); axisY = @('ForestGreen', 1.6); axisZ = @('Blue', 1.6); axisRing = @('Black', 1)
        profile = @('SteelBlue', 2.2); corner = @('Black', 1)
        hw = @('DarkOrange', 1.4); hwHidden = @('DarkOrange', 1.3, 'Dash'); hwBad = @('DarkRed', 1.5)
    }
    foreach ($k in $pens.Keys) {
        $d = $pens[$k]
        $p = New-Object System.Drawing.Pen ([System.Drawing.Color]::($d[0])), ([single]$d[1])
        if ($d.Count -gt 2) { $p.DashStyle = [System.Drawing.Drawing2D.DashStyle]::($d[2]) }
        $TwinGdi["pen.$k"] = $p
    }
    $fills = @{ face = 'White'; holeBad = 'Red'; hwBad = 'Red'; corner = 'DimGray'; arrow = 'DimGray'; axisX = 'Red'; axisY = 'ForestGreen'; axisZ = 'Blue' }
    foreach ($k in $fills.Keys) { $TwinGdi["fill.$k"] = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::($fills[$k])) }
    $texts = @{
        label = @('Black', 9, 'Bold'); sub = @('DimGray', 8, 'Regular'); caption = @('DimGray', 9, 'Bold')
        bom = @('Black', 8, 'Regular'); bomHead = @('Black', 8, 'Bold'); bomTitle = @('Black', 9, 'Bold')
        axisLabel = @('Black', 8, 'Bold'); note = @('Black', 8, 'Regular'); empty = @('Gray', 11, 'Regular')
    }
    foreach ($k in $texts.Keys) {
        $d = $texts[$k]
        $TwinGdi["text.$k"] = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::($d[0]))
        $TwinGdi["font.$k"] = New-Object System.Drawing.Font('Segoe UI', ([single]$d[1]), [System.Drawing.FontStyle]::($d[2]))
    }
    foreach ($a in @(@('center', 'Center'), @('left', 'Near'))) {
        $f = New-Object System.Drawing.StringFormat
        $f.Alignment = [System.Drawing.StringAlignment]::($a[1])
        $f.LineAlignment = [System.Drawing.StringAlignment]::Center
        $TwinGdi["format.$($a[0])"] = $f
    }
}

function Invoke-TwinPaint($G, $Shapes) {
    if (-not $Shapes) { return }
    if ($TwinGdi.Count -eq 0) { Initialize-TwinGdi }
    $G.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $G.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    foreach ($sh in $Shapes) {
        if ($null -eq $sh.Points) {
            $saved = $G.Save()
            $G.TranslateTransform([single]$sh.X, [single]$sh.Y)
            if ($sh.Angle) { $G.RotateTransform([single]$sh.Angle) }
            $G.DrawString($sh.Text, $TwinGdi["font.$($sh.Kind)"], $TwinGdi["text.$($sh.Kind)"], [single]0, [single]$sh.Dy, $TwinGdi["format.$($sh.Align)"])
            $G.Restore($saved)
            continue
        }
        $n = $sh.Points.Length / 2
        $pts = New-Object 'System.Drawing.PointF[]' $n
        for ($k = 0; $k -lt $n; $k++) { $pts[$k] = New-Object System.Drawing.PointF ([single]$sh.Points[2 * $k]), ([single]$sh.Points[2 * $k + 1]) }
        $fill = $TwinGdi["fill.$($sh.Kind)"]
        $pen = $TwinGdi["pen.$($sh.Kind)"]
        if ($sh.Closed) {
            if ($fill) { $G.FillPolygon($fill, $pts) }
            if ($pen) { $G.DrawPolygon($pen, $pts) }
        } elseif ($pen) {
            $G.DrawLines($pen, $pts)
        }
    }
}

# ------------------------------------------------------------------ SVG file

$TwinSvgFont = 'font-family="Segoe UI, Arial, sans-serif"'
$TwinSvgStyle = @{
    face       = 'fill="#ffffff" stroke="#000000" stroke-width="1.3" stroke-linejoin="round"'
    edge       = 'fill="none" stroke="#000000" stroke-width="1"'
    inner      = 'fill="none" stroke="#808080" stroke-width="1" stroke-dasharray="6 4"'
    hidden     = 'fill="none" stroke="#808080" stroke-width="1" stroke-dasharray="6 4"'
    hole       = 'fill="none" stroke="#000000" stroke-width="1.1"'
    holeBad    = 'fill="#ff0000" stroke="#8b0000" stroke-width="1.5"'
    hiddenHole = 'fill="none" stroke="#808080" stroke-width="1" stroke-dasharray="3 2"'
    center     = 'fill="none" stroke="#808080" stroke-width="0.8" stroke-dasharray="6 2 1 2"'
    dim        = 'fill="none" stroke="#696969" stroke-width="1"'
    ext        = 'fill="none" stroke="#c0c0c0" stroke-width="1"'
    arrow      = 'fill="#696969" stroke="none"'
    table      = 'fill="none" stroke="#000000" stroke-width="1"'
    frame      = 'fill="none" stroke="#000000" stroke-width="1.6"'
    axisX      = 'fill="#ff0000" stroke="#ff0000" stroke-width="1.6"'
    axisY      = 'fill="#228b22" stroke="#228b22" stroke-width="1.6"'
    axisZ      = 'fill="#0000ff" stroke="#0000ff" stroke-width="1.6"'
    axisRing   = 'fill="none" stroke="#000000" stroke-width="1"'
    profile    = 'fill="none" stroke="#4682b4" stroke-width="2.2" stroke-linecap="round"'
    corner     = 'fill="#696969" stroke="#000000" stroke-width="1"'
    hw         = 'fill="none" stroke="#ff8c00" stroke-width="1.4"'
    hwHidden   = 'fill="none" stroke="#ff8c00" stroke-width="1.3" stroke-dasharray="2.5 1.5"'
    hwBad      = 'fill="#ff0000" stroke="#8b0000" stroke-width="1.5"'
    label      = "$TwinSvgFont font-size=""13"" font-weight=""bold"" fill=""#000000"""
    sub        = "$TwinSvgFont font-size=""11"" fill=""#696969"""
    caption    = "$TwinSvgFont font-size=""14"" font-weight=""bold"" fill=""#696969"""
    bom        = "$TwinSvgFont font-size=""12"" fill=""#000000"""
    bomHead    = "$TwinSvgFont font-size=""12"" font-weight=""bold"" fill=""#000000"""
    bomTitle   = "$TwinSvgFont font-size=""14"" font-weight=""bold"" fill=""#000000"""
    axisLabel  = "$TwinSvgFont font-size=""11"" font-weight=""bold"" fill=""#000000"""
    note       = "$TwinSvgFont font-size=""12"" fill=""#000000"""
    empty      = "$TwinSvgFont font-size=""15"" fill=""#808080"""
}

function Export-TwinSvg([string]$Path, $Shapes, [double]$ViewW, [double]$ViewH, [string]$Title) {
    $n = { param([double]$v) $v.ToString('0.#', $Invariant) }
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$sb.AppendLine("<svg xmlns=""http://www.w3.org/2000/svg"" width=""$(& $n $ViewW)"" height=""$(& $n $ViewH)"" viewBox=""0 0 $(& $n $ViewW) $(& $n $ViewH)"">")
    [void]$sb.AppendLine("  <title>$([Security.SecurityElement]::Escape($Title))</title>")
    [void]$sb.AppendLine('  <rect width="100%" height="100%" fill="#ffffff"/>')
    foreach ($sh in $Shapes) {
        $style = $TwinSvgStyle[$sh.Kind]
        if ($null -eq $sh.Points) {
            $anchor = $(if ($sh.Align -eq 'left') { 'start' } else { 'middle' })
            [void]$sb.AppendLine(("  <text transform=""translate({0} {1}) rotate({2})"" y=""{3}"" text-anchor=""{4}"" dominant-baseline=""middle"" {5}>{6}</text>" -f
                (& $n $sh.X), (& $n $sh.Y), (& $n $sh.Angle), (& $n $sh.Dy), $anchor, $style, [Security.SecurityElement]::Escape($sh.Text)))
            continue
        }
        $pts = for ($k = 0; $k -lt $sh.Points.Length; $k += 2) { "$(& $n $sh.Points[$k]),$(& $n $sh.Points[$k + 1])" }
        if ($sh.Closed) {
            [void]$sb.AppendLine("  <polygon points=""$($pts -join ' ')"" $style/>")
        } else {   # open lines are never filled
            [void]$sb.AppendLine("  <polyline points=""$($pts -join ' ')"" $($style -replace 'fill="[^"]*"', 'fill="none"')/>")
        }
    }
    [void]$sb.AppendLine('</svg>')
    [IO.File]::WriteAllText($Path, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
}
