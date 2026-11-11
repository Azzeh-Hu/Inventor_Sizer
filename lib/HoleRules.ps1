# Hole layout rules. Positions are in the -Au frame, measured from the start of the wall.
#
#   0 holes   -> none (HoleOn = 0, the model suppresses the hole features)
#   1 hole    -> in the middle of the wall
#   2 holes   -> HoleEdge from each end
#   n holes   -> 2 at HoleEdge, the rest evenly spaced: pitch = (span - 2*HoleEdge) / (n - 1)
#   width wall: even counts only above 2; 4 = the 2 edge holes + a centre pair at
#               span/2 +- MidOffset (MidOffset = HoleEdge - thickness - Clearance = 48.5 mm)

function New-WallLayout([string]$Wall, [double]$Span, [double]$Holes, [double]$Edge,
                        [double]$MidOffset, [double]$MinGap, [bool]$WidthRules) {
    $r = [ordered]@{
        Wall = $Wall; Span = $Span; Holes = $Holes
        Start = $Edge; Pitch = [math]::Max($Span - 2 * $Edge, 1); Count = 1; On = 1; MidOn = 0
        Positions = @(); Errors = @()
    }
    $n = [int][math]::Round($Holes)
    if ($Holes -lt 0 -or $Holes -ne $n) {
        $r.Errors = @("$Wall wall: the hole count must be a whole number, 0 or more (got $(Format-Number $Holes)).")
        return $r
    }
    if ($WidthRules -and $n -gt 2 -and ($n % 2) -ne 0) {
        $r.Errors = @("$Wall wall: use 0, 1, 2, 4, 6, ... holes (got $n).")
        return $r
    }
    if ($n -ge 2 -and $Span - 2 * $Edge -le 0) {
        $r.Errors = @("$Wall wall: HoleEdge $(Format-Number $Edge) mm leaves no room on a $(Format-Number $Span) mm wall.")
        return $r
    }

    if ($n -eq 0) {
        $r.On = 0
    } elseif ($n -eq 1) {
        $r.Start = $Span / 2
        $r.Positions = @($Span / 2)
    } elseif ($WidthRules -and $n -eq 4) {
        $r.Count = 2
        $r.Pitch = $Span - 2 * $Edge
        $r.MidOn = 1
        $r.Positions = @($Edge, ($Span / 2 - $MidOffset), ($Span / 2 + $MidOffset), ($Span - $Edge))
        if ($Span / 2 - $MidOffset -le $Edge) {
            $r.Errors += "$Wall wall: $(Format-Number $Span) mm is too narrow for 4 holes (the centre pair would pass the edge holes)."
        }
    } else {
        $pitch = ($Span - 2 * $Edge) / ($n - 1)
        $r.Count = $n
        $r.Pitch = $pitch
        $r.Positions = @(for ($i = 0; $i -lt $n; $i++) { $Edge + $i * $pitch })
    }

    $sorted = @($r.Positions | Sort-Object)
    for ($i = 1; $i -lt $sorted.Count; $i++) {
        $gap = $sorted[$i] - $sorted[$i - 1]
        if ($gap -lt $MinGap) {
            $r.Errors += "$Wall wall: holes $i and $($i + 1) are only $(Format-Number $gap) mm apart (minimum $(Format-Number $MinGap) mm)."
            break
        }
    }
    $r
}

# Values: hashtable Name -> number. Returns both wall layouts, all errors, and the
# parameters the app writes into Inventor (HoleStartL, HolePitchL, ...).
function Get-HoleLayout([hashtable]$Values, $Settings) {
    foreach ($k in 'Width', 'Length', 'HoleEdge', 'Clearance', 'HolesL', 'HolesW') {
        if (-not $Values.ContainsKey($k)) { throw "Parameter '$k' is missing." }
    }
    $edge = $Values['HoleEdge']
    $mid  = $edge - [double]$Settings.thickness - $Values['Clearance']
    $gap  = [double]$Settings.minHoleGap

    $len = New-WallLayout 'Length' $Values['Length'] $Values['HolesL'] $edge $mid $gap $false
    $wid = New-WallLayout 'Width'  $Values['Width']  $Values['HolesW'] $edge $mid $gap $true

    $params = @(
        (New-Param 'HoleStartL' $len.Start 'mm'), (New-Param 'HolePitchL' $len.Pitch 'mm'),
        (New-Param 'HoleCountL' $len.Count 'ul'), (New-Param 'HoleOnL'    $len.On    'ul'),
        (New-Param 'HoleStartW' $wid.Start 'mm'), (New-Param 'HolePitchW' $wid.Pitch 'mm'),
        (New-Param 'HoleCountW' $wid.Count 'ul'), (New-Param 'HoleOnW'    $wid.On    'ul'),
        (New-Param 'HoleMidOnW' $wid.MidOn 'ul')
    )
    [ordered]@{
        Length    = $len
        Width     = $wid
        MidOffset = $mid
        Errors    = @(@($len.Errors) + @($wid.Errors))
        Params    = $params
    }
}
