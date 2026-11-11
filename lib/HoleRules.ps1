# Hole layout rules. Positions are in the -Au frame, measured from the start of the wall.
#
#   0 holes   -> none (HoleOn = 0, the model suppresses the hole features)
#   1 hole    -> in the middle of the wall
#   2 holes   -> HoleEdge from each end
#   n holes   -> 2 at HoleEdge, the rest evenly spaced: pitch = (span - 2*HoleEdge) / (n - 1)
#   width wall: even counts only above 2; 4 = the 2 edge holes + a centre pair at
#               span/2 +- MidOffset (MidOffset = HoleEdge - thickness - Clearance = 48.5 mm)
#   split inner panel (width above the split width): width wall 0 or 4 holes only

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

# Inner-panel rule (all types): a panel wider than the split width gets its -In part split in
# 2 equal halves, placed side by side across the width.
function Get-InnerSplit([double]$Width, $Settings) {
    $limit = [double]$Settings.splitWidth
    if ($limit -gt 0 -and $Width -gt $limit) { 2 } else { 1 }
}

# Values: hashtable Name -> number. Returns both wall layouts, the inner-panel split, all
# errors (block Apply) and notes (shown only), and the parameters the app writes into Inventor
# (HoleStartL, HolePitchL, ..., InSplit). $Standard is optional (type rules: height, minHolesL).
function Get-HoleLayout([hashtable]$Values, $Settings, $Standard = $null) {
    foreach ($k in 'Width', 'Length', 'HoleEdge', 'Clearance', 'HolesL', 'HolesW') {
        if (-not $Values.ContainsKey($k)) { throw "Parameter '$k' is missing." }
    }
    $edge  = $Values['HoleEdge']
    $thick = [double]$Settings.thickness
    $mid   = $edge - $thick - $Values['Clearance']
    $gap   = [double]$Settings.minHoleGap
    $notes = @(); $errors = @()

    $split  = Get-InnerSplit $Values['Width'] $Settings
    $inset  = 2 * $thick + 2 * $Values['Clearance']
    $innerW = ($Values['Width'] - $inset) / $split
    $innerL = $Values['Length'] - $inset

    # Split inner panel: each half has a hole at MidOffset from both of its ends, so the tray
    # needs the centre pair (W/2 +- MidOffset) next to the 2 edge holes.
    $holesW = $Values['HolesW']
    if ($split -ge 2) {
        $notes += "Wider than $(Format-Number $Settings.splitWidth) mm: the inner panel is split in 2 halves of $(Format-Number $innerW) mm."
        if ($holesW -eq 2) {
            $holesW = 4
            $notes += 'Width wall: 2 holes become 4 (the centre pair holds the inner ends of the 2 halves).'
        } elseif ($holesW -ne 0 -and $holesW -ne 4) {
            $errors += "Width wall: with a split inner panel use 0 or 4 holes (got $(Format-Number $holesW))."
            $holesW = 0
        }
    }

    $len = New-WallLayout 'Length' $Values['Length'] $Values['HolesL'] $edge $mid $gap $false
    $wid = New-WallLayout 'Width'  $Values['Width']  $holesW           $edge $mid $gap $true

    if ($Standard) {
        if ($Standard.MinHolesL -gt 0 -and $Values['HolesL'] -lt $Standard.MinHolesL) {
            $errors += "Length wall: $($Standard.Family) panels need at least $($Standard.MinHolesL) holes (got $(Format-Number $Values['HolesL']))."
        }
        if ($Standard.Height -gt 0 -and $Values.ContainsKey('Height') -and $Values['Height'] -ne $Standard.Height) {
            $notes += "$($Standard.Family) panels are normally $(Format-Number $Standard.Height) mm high (this one is $(Format-Number $Values['Height']) mm)."
        }
    }

    # Width-wall pattern of the -In part: the same as the tray's when it is one piece; for a
    # half it is 2 holes, MidOffset from each end of the half, and no centre pair.
    $inPitch = $wid.Pitch; $inCount = $wid.Count; $inMidOn = $wid.MidOn
    if ($split -ge 2) {
        $inMidOn = 0
        if ($wid.On) { $inCount = 2; $inPitch = [math]::Max($innerW - 2 * $mid, 1) } else { $inCount = 1 }
    }

    $params = @(
        (New-Param 'HoleStartL' $len.Start 'mm'), (New-Param 'HolePitchL' $len.Pitch 'mm'),
        (New-Param 'HoleCountL' $len.Count 'ul'), (New-Param 'HoleOnL'    $len.On    'ul'),
        (New-Param 'HoleStartW' $wid.Start 'mm'), (New-Param 'HolePitchW' $wid.Pitch 'mm'),
        (New-Param 'HoleCountW' $wid.Count 'ul'), (New-Param 'HoleOnW'    $wid.On    'ul'),
        (New-Param 'HoleMidOnW' $wid.MidOn 'ul'),
        (New-Param 'InSplit' $split 'ul'),
        (New-Param 'InHolePitchW' $inPitch 'mm'), (New-Param 'InHoleCountW' $inCount 'ul'),
        (New-Param 'InHoleMidOnW' $inMidOn 'ul')
    )
    [ordered]@{
        Length     = $len
        Width      = $wid
        MidOffset  = $mid
        Split      = $split
        InnerWidth = $innerW
        InnerLength = $innerL
        Errors     = @($errors + @($len.Errors) + @($wid.Errors))
        Notes      = $notes
        Params     = $params
    }
}
