# Door hardware holes (hinges, handles, locks): learned once from a master in Inventor, then
# re-placed for every size in the drawing.
#
#   Read-HardwareCircles     COM: every full circle edge of the panel's parts, in assembly space
#   ConvertTo-HoleTemplate   circles -> hole template: each hole measured from its nearest
#                            panel edge (or from the middle), so it keeps that distance at any size
#   Get-HardwareHoles        template + W x L x H -> hole positions for the drawing
#
# The wall holes made by the hole rules (HoleDia at HoleZ) are left out; they are drawn from
# the rules. Templates are stored per type in standards\hardware\<type id>.json.

$HardwareMidFactor = 0.5   # a hole is measured from the middle only when that is less than half its edge distance

function New-HardwareHole([string]$Wall, [string]$Side, [double]$Dia, $U, $V) {
    [pscustomobject]@{ Wall = $Wall; Side = $Side; Dia = $Dia; U = $U; V = $V }
}

# Distance from the start, the end or the middle of a span, whichever describes the position best.
function Get-Anchor([double]$Pos, [double]$Span) {
    $start = $Pos; $end = $Span - $Pos; $mid = $Pos - $Span / 2
    $edge = [math]::Min([math]::Abs($start), [math]::Abs($end))
    if ([math]::Abs($mid) -lt $HardwareMidFactor * $edge) { return [pscustomobject]@{ From = 'mid'; Value = [math]::Round($mid, 3) } }
    if ([math]::Abs($start) -le [math]::Abs($end)) { return [pscustomobject]@{ From = 'start'; Value = [math]::Round($start, 3) } }
    [pscustomobject]@{ From = 'end'; Value = [math]::Round($end, 3) }
}

function Resolve-Anchor($Anchor, [double]$Span) {
    switch ($Anchor.From) {
        'end' { $Span - [double]$Anchor.Value }
        'mid' { $Span / 2 + [double]$Anchor.Value }
        default { [double]$Anchor.Value }
    }
}

# Which axis of a box (sizes in mm) is the width, the length and the height. Returns e.g.
# @(1, 0, 2): width along axis 1 (Y), length along X, height along Z.
function Get-PanelAxes([double[]]$Size, [double]$W, [double]$L, [double]$H) {
    $want = @($W, $L, $H); $axes = @(-1, -1, -1); $taken = @{}
    foreach ($k in 2, 0, 1) {   # height first: it is the most distinct
        $best = -1; $bestDiff = [double]::MaxValue
        for ($a = 0; $a -lt 3; $a++) {
            if ($taken[$a]) { continue }
            $d = [math]::Abs($Size[$a] - $want[$k])
            if ($d -lt $bestDiff) { $best = $a; $bestDiff = $d }
        }
        if ($bestDiff -gt 1.5) {
            throw ("The panel in Inventor is $(($Size | ForEach-Object { Format-Number $_ }) -join ' x ') mm, which does not match " +
                   "the name ($(Format-Number $W) x $(Format-Number $L) x $(Format-Number $H)). Open the master the name describes.")
        }
        $axes[$k] = $best; $taken[$best] = $true
    }
    , $axes
}

# $Circles: Center (3 mm values, assembly space), Normal (3 values), Dia (mm).
# $BoxMin / $BoxSize: the outer tray's box in assembly space (mm).
function ConvertTo-HoleTemplate($Circles, [double[]]$BoxMin, [double[]]$BoxSize, [double]$W, [double]$L, [double]$H,
                                [double]$RuleDia, [double]$RuleZ) {
    $axes = Get-PanelAxes $BoxSize $W $L $H
    $seen = @{}
    $holes = New-Object System.Collections.Generic.List[object]
    $skipped = 0
    foreach ($c in $Circles) {
        $p = @(0, 1, 2 | ForEach-Object { $c.Center[$axes[$_]] - $BoxMin[$axes[$_]] })   # x (width), y (length), z (height) in mm
        $n = @(0, 1, 2 | ForEach-Object { [math]::Abs($c.Normal[$axes[$_]]) })
        $k = [array]::IndexOf($n, ($n | Measure-Object -Maximum).Maximum)
        if ($n[$k] -lt 0.99) { continue }   # tilted circle: not a drilled hole
        switch ($k) {
            2 { $wall = 'face';   $side = $(if ($p[2] -lt $H / 2) { 'bottom' } else { 'top' }); $u = $p[0]; $v = $p[1]; $su = $W; $sv = $L }
            0 { $wall = 'length'; $side = $(if ($p[0] -lt $W / 2) { 'x0' } else { 'xW' });       $u = $p[1]; $v = $p[2]; $su = $L; $sv = $H }
            1 { $wall = 'width';  $side = $(if ($p[1] -lt $L / 2) { 'y0' } else { 'yL' });       $u = $p[0]; $v = $p[2]; $su = $W; $sv = $H }
        }
        # The 2 rims of one hole, and the -Au and -In holes that line up, give the same key.
        $key = '{0}|{1}|{2:0.0}|{3:0.0}|{4:0.00}' -f $wall, $side, $u, $v, $c.Dia
        if ($seen[$key]) { continue }
        $seen[$key] = $true
        if ($wall -ne 'face' -and [math]::Abs($c.Dia - $RuleDia) -lt 0.05 -and [math]::Abs($v - $RuleZ) -lt 1.6) { $skipped++; continue }
        $hu = Get-Anchor $u $su
        $hv = $(if ($wall -eq 'face') { Get-Anchor $v $sv } else { [pscustomobject]@{ From = 'start'; Value = [math]::Round($v, 3) } })
        $holes.Add((New-HardwareHole $wall $side ([math]::Round($c.Dia, 3)) $hu $hv))
    }
    [pscustomobject]@{ Holes = $holes.ToArray(); RuleHolesSkipped = $skipped }
}

# Hole positions for a size: X (width), Y (length), Z (height) in mm, the axis the hole runs
# along, and Bad = the hole falls outside the panel at this size.
function Get-HardwareHoles($Template, [double]$W, [double]$L, [double]$H) {
    if (-not $Template) { return @() }
    $list = @(foreach ($hole in @($Template.Holes)) {
        $r = [double]$hole.Dia / 2
        switch ($hole.Wall) {
            'face'   { $x = Resolve-Anchor $hole.U $W; $y = Resolve-Anchor $hole.V $L; $z = $(if ($hole.Side -eq 'top') { $H } else { 0 }); $axis = 'z'
                       $bad = $x - $r -lt 0 -or $x + $r -gt $W -or $y - $r -lt 0 -or $y + $r -gt $L }
            'length' { $y = Resolve-Anchor $hole.U $L; $z = Resolve-Anchor $hole.V $H; $x = $(if ($hole.Side -eq 'xW') { $W } else { 0 }); $axis = 'x'
                       $bad = $y - $r -lt 0 -or $y + $r -gt $L }
            default  { $x = Resolve-Anchor $hole.U $W; $z = Resolve-Anchor $hole.V $H; $y = $(if ($hole.Side -eq 'yL') { $L } else { 0 }); $axis = 'y'
                       $bad = $x - $r -lt 0 -or $x + $r -gt $W }
        }
        [pscustomobject]@{ X = $x; Y = $y; Z = $z; Axis = $axis; Dia = [double]$hole.Dia; Wall = $hole.Wall; Side = $hole.Side
                           Group = [string]$hole.Group; Bad = [bool]$bad; Overlap = $false }
    })
    # Holes in the same face that run into each other (e.g. the 2 handles on a short door).
    for ($i = 0; $i -lt $list.Count; $i++) {
        for ($j = $i + 1; $j -lt $list.Count; $j++) {
            $a = $list[$i]; $b = $list[$j]
            if ($a.Wall -ne $b.Wall -or $a.Side -ne $b.Side) { continue }
            $d = [math]::Sqrt([math]::Pow($a.X - $b.X, 2) + [math]::Pow($a.Y - $b.Y, 2) + [math]::Pow($a.Z - $b.Z, 2))
            if ($d -lt ($a.Dia + $b.Dia) / 2) { $a.Bad = $true; $b.Bad = $true; $a.Overlap = $true; $b.Overlap = $true }
        }
    }
    , $list
}

# Holes that fall outside the panel at this size (shown as a note).
function Get-HardwareNotes($Holes) {
    $over = @(@($Holes) | Where-Object { $_.Overlap }).Count
    $out = @(@($Holes) | Where-Object { $_.Bad -and -not $_.Overlap }).Count
    $notes = @()
    if ($out) { $notes += "Door hardware: $out hole(s) fall outside the panel at this size (red in the drawing)." }
    if ($over) { $notes += "Door hardware: $over hole(s) run into each other at this size, e.g. the 2 handles on a short door (red in the drawing)." }
    $notes   # plain output: @(Get-HardwareNotes ...) is then a flat list
}

# ------------------------------------------------------------------ files

function Get-HardwarePath([string]$Dir, $Standard) { Join-Path (Join-Path $Dir 'hardware') "$($Standard.Id).json" }

function Read-HardwareTemplate([string]$Dir, $Standard) {
    if (-not $Standard) { return $null }
    $path = Get-HardwarePath $Dir $Standard
    if (-not (Test-Path $path)) { return $null }
    try { Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { Write-Log "Hardware file $path could not be read: $($_.Exception.Message)"; $null }
}

function Save-HardwareTemplate([string]$Dir, $Standard, $Template, [string]$Source) {
    $path = Get-HardwarePath $Dir $Standard
    $folder = Split-Path $path
    if (-not (Test-Path $folder)) { [void](New-Item -ItemType Directory -Path $folder) }
    $doc = [ordered]@{
        type    = $Standard.Id
        source  = $Source
        learned = (Get-Date).ToString('yyyy-MM-dd HH:mm')
        notes   = 'U / V: hole position. from = start, end or mid of the span: width (U) and length (V) on the face; along the wall (U) and height (V) on a wall.'
        holes   = @($Template.Holes)
    }
    $doc | ConvertTo-Json -Depth 6 | Set-Content $path -Encoding UTF8
    $path
}

# ------------------------------------------------------------------ Inventor

# Every full circle edge of the occurrences' bodies, in assembly space. The occurrence's own
# bodies (proxies) give their geometry in assembly coordinates, so no transformation is needed.
function Read-HardwareCircles($Occurrences) {
    $list = New-Object System.Collections.Generic.List[object]
    foreach ($occ in $Occurrences) {
        $bodies = $occ.SurfaceBodies
        for ($b = 1; $b -le $bodies.Count; $b++) {
            $edges = $bodies.Item($b).Edges
            for ($e = 1; $e -le $edges.Count; $e++) {
                try {
                    $g = $edges.Item($e).Geometry
                    if ($null -eq $g.Radius -or $null -eq $g.Center -or $null -eq $g.Normal) { continue }
                    if ($null -ne $g.SweepAngle) { continue }   # arc (bend, rounded corner), not a full circle
                    $list.Add([pscustomobject]@{
                        Center = [double[]]@(($g.Center.X * 10), ($g.Center.Y * 10), ($g.Center.Z * 10))
                        Normal = [double[]]@($g.Normal.X, $g.Normal.Y, $g.Normal.Z)
                        Dia    = $g.Radius * 20
                    })
                } catch { }
            }
        }
    }
    , $list.ToArray()
}

# Reads the hardware holes of the active panel assembly ($Standard's parts) and saves them as
# the type's template. Width, length and height come from the assembly name.
function Invoke-LearnHardware($Standards, $Standard, [string]$StandardsDir, [double]$RuleDia, [double]$RuleZ) {
    $asm = Get-ActiveAssembly
    $name = [IO.Path]::GetFileNameWithoutExtension($asm.FullFileName)
    $m = $SizeRegex.Match($name)
    if (-not $m.Success) { throw "'$name' has no size in its name." }
    $W = [double]::Parse($m.Groups[1].Value, $Invariant); $L = [double]::Parse($m.Groups[2].Value, $Invariant); $H = [double]::Parse($m.Groups[3].Value, $Invariant)
    $parsed = Split-PanelName $Standards $name
    if ($parsed.Standard -and $parsed.Standard.Id -ne $Standard.Id) {
        throw "The active assembly is a $($parsed.Standard.Name), but the selected type is $($Standard.Name)."
    }

    $outer = Get-OccurrencesBySuffix $asm (Get-PartSuffixes $Standard 'outer')
    if ($outer.Count -eq 0) { throw "No outer tray (file name ending in $((Get-PartSuffixes $Standard 'outer') -join ' or ')) in $name." }
    $inner = Get-OccurrencesBySuffix $asm (Get-PartSuffixes $Standard 'inner')
    $box = $outer[0].RangeBox
    $min = [double[]]@(($box.MinPoint.X * 10), ($box.MinPoint.Y * 10), ($box.MinPoint.Z * 10))
    $size = [double[]]@((($box.MaxPoint.X - $box.MinPoint.X) * 10), (($box.MaxPoint.Y - $box.MinPoint.Y) * 10), (($box.MaxPoint.Z - $box.MinPoint.Z) * 10))

    $circles = Read-HardwareCircles (@($outer) + @($inner))
    $template = ConvertTo-HoleTemplate $circles $min $size $W $L $H $RuleDia $RuleZ
    $path = Save-HardwareTemplate $StandardsDir $Standard $template $asm.FullFileName
    $byWall = @($template.Holes | Group-Object Wall | ForEach-Object { "$($_.Count) on the $($_.Name)" }) -join ', '
    Write-Log "Learned $(@($template.Holes).Count) hardware hole(s) from $name ($byWall); skipped $($template.RuleHolesSkipped) wall hole(s) made by the hole rules."
    foreach ($hole in $template.Holes) {
        Write-Log ("  {0} {1}: dia {2}, {3} {4} / {5} {6}" -f $hole.Wall, $hole.Side, (Format-Number $hole.Dia), $hole.U.From, (Format-Number $hole.U.Value), $hole.V.From, (Format-Number $hole.V.Value))
    }
    Write-Log "Saved $path"
    $template
}
