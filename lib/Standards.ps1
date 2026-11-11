# Product standards: one JSON file per family in .\standards (FP.json, TP.json, RP.json).
# A type sets the name prefix, the panel height it is recognised by, the master assembly new
# sizes start from, type defaults (e.g. HoleZ) and the parts listed in the bill of materials.
#
# Assembly names look like  [project ]<prefix><W>x<L>x<H>[ tail],  e.g. "6357-3 TP-519x1194x42".
# The project text in front of the prefix (the "lead") is kept when the size or type changes.

function New-StandardPart([string]$Suffix, [string]$Description, [string]$Role, [int]$Qty, [string]$SplitSuffix = '') {
    [pscustomobject]@{ Suffix = $Suffix; SplitSuffix = $SplitSuffix; Description = $Description; Role = $Role; Qty = $Qty }
}

function New-Standard([string]$Id, [string]$Name, [string]$Family, [string]$Prefix, [string]$Master, [string]$File, $Parts) {
    [pscustomobject]@{
        Id = $Id; Name = $Name; Family = $Family; Prefix = $Prefix; Master = $Master; File = $File; Parts = @($Parts)
        Height = 0.0        # panel height the type is recognised by (0 = any)
        Defaults = @{}      # parameter values set when the type is selected, e.g. HoleZ
        MinHolesL = 0       # smallest allowed hole count per length wall
        SplitAxis = 'auto'  # part axis of the inner panel's width: auto, X, Y or Z
    }
}

# Used when no standards file can be read, so the app still works.
$BuiltInStandard = New-Standard 'FP' 'FP - Festes Paneel' 'FP' 'FP-' '' '(built in)' @(
    (New-StandardPart '-Au' 'Outer tray (Au)' 'outer' 1), (New-StandardPart '-In' 'Inner panel (In)' 'inner' 1))
$BuiltInStandard.Height = 47.0

function Get-JsonValue($Object, [string]$Name, $Default) {
    if ($Object -and $Object.PSObject.Properties.Name -contains $Name -and $null -ne $Object.$Name) { $Object.$Name } else { $Default }
}

function Read-Standards([string]$Dir, [string]$Root) {
    $list = New-Object System.Collections.Generic.List[object]
    if (Test-Path $Dir) {
        foreach ($file in Get-ChildItem $Dir -Filter '*.json' | Sort-Object Name) {
            try { $json = Get-Content $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json }
            catch { Write-Log "Standard $($file.Name) could not be read: $($_.Exception.Message)"; continue }
            foreach ($v in @($json.variants)) {
                if (-not (Get-JsonValue $v 'enabled' $true)) { continue }
                if (-not $v.id -or -not $v.prefix -or -not $v.parts) {
                    Write-Log "Standard $($file.Name): a type is missing id, prefix or parts - skipped"
                    continue
                }
                $master = [string]$v.master
                if ($master -and -not [IO.Path]::IsPathRooted($master)) { $master = Join-Path $Root $master }
                $parts = foreach ($p in $v.parts) {
                    New-StandardPart ([string]$p.suffix) ([string]$p.description) ([string]$p.role) ([int]$p.qty) ([string](Get-JsonValue $p 'splitSuffix' ''))
                }
                $s = New-Standard ([string]$v.id) ([string]$v.name) ([string]$json.family) ([string]$v.prefix) $master $file.Name $parts
                $s.Height    = [double](Get-JsonValue $v 'height' 0)
                $s.MinHolesL = [int](Get-JsonValue $v 'minHolesL' 0)
                $s.SplitAxis = [string](Get-JsonValue $v 'splitAxis' 'auto')
                $defaults = Get-JsonValue $v 'defaults' $null
                if ($defaults) { foreach ($d in $defaults.PSObject.Properties) { $s.Defaults[$d.Name] = [double]$d.Value } }
                $list.Add($s)
            }
        }
    }
    if ($list.Count -eq 0) {
        $list.Add($BuiltInStandard)
        Write-Log 'No standards found in .\standards - using the built-in FP standard'
    }
    , $list.ToArray()
}

# Splits an assembly name around its size: "6357-3 TP-519x1194x42" ->
# Lead "6357-3 ", Prefix "TP-", Token "519x1194x42", Tail "", Standard = the TP type.
# The type is the one whose prefix sits directly in front of the size (longest wins); without a
# known prefix it is the type with the same height (TP 42, RP 44, FP 47). Returns $null when
# the name has no size.
function Split-PanelName($Standards, [string]$Name) {
    $m = $SizeRegex.Match($Name)
    if (-not $m.Success) { return $null }
    $before = $Name.Substring(0, $m.Index)
    $best = $null
    foreach ($s in $Standards) {
        if ($before.EndsWith($s.Prefix, [StringComparison]::OrdinalIgnoreCase) -and
            ($null -eq $best -or $s.Prefix.Length -gt $best.Prefix.Length)) { $best = $s }
    }
    $prefix = ''
    if ($best) {
        $prefix = $before.Substring($before.Length - $best.Prefix.Length)
    } else {
        $height = [double]::Parse($m.Groups[3].Value, $Invariant)
        foreach ($s in $Standards) { if ($s.Height -gt 0 -and $s.Height -eq $height) { $best = $s; break } }
    }
    [pscustomobject]@{
        Lead = $before.Substring(0, $before.Length - $prefix.Length); Prefix = $prefix
        Token = $m.Value; Tail = $Name.Substring($m.Index + $m.Length); Standard = $best
    }
}

function Find-StandardForName($Standards, [string]$Name) {
    $parsed = Split-PanelName $Standards $Name
    if ($parsed) { $parsed.Standard } else { $null }
}

# The same name for another type: the prefix in front of the size is swapped, the project lead
# and the size stay.
function Set-PanelNamePrefix($Standards, [string]$Name, [string]$Prefix) {
    $parsed = Split-PanelName $Standards $Name
    if (-not $parsed) { return $Name }
    $parsed.Lead + $Prefix + $parsed.Token + $parsed.Tail
}

# File name or Part Number of a part for the new assembly name: the size is swapped, and so is
# the project lead when the text starts with it (or with the size, when there was no lead).
function Convert-PanelText([string]$Text, $Old, $New) {
    $out = $Text.Replace($Old.Token, $New.Token)
    if ($Old.Lead -ne $New.Lead) {
        if ($Old.Lead -and $out.StartsWith($Old.Lead, [StringComparison]::OrdinalIgnoreCase)) {
            $out = $New.Lead + $out.Substring($Old.Lead.Length)
        } elseif (-not $Old.Lead -and $out.StartsWith($New.Token)) {
            $out = $New.Lead + $out
        }
    }
    $out
}

function Get-PartSuffixes($Standard, [string]$Role) {
    @(foreach ($p in $Standard.Parts) {
        if ($p.Role -ne $Role) { continue }
        if ($p.SplitSuffix) { $p.SplitSuffix }
        $p.Suffix
    })
}

# Inner-panel file name (without extension) for the split state: "...-In" <-> "...-In-2T" for
# types that name the split part differently. Other names are returned unchanged.
function Convert-SplitName($Standard, [string]$BaseName, [int]$Split) {
    foreach ($p in $Standard.Parts) {
        if ($p.Role -ne 'inner' -or -not $p.SplitSuffix) { continue }
        $want = $(if ($Split -ge 2) { $p.SplitSuffix } else { $p.Suffix })
        foreach ($have in @($p.SplitSuffix, $p.Suffix)) {
            if ($BaseName.EndsWith($have, [StringComparison]::OrdinalIgnoreCase)) {
                return $BaseName.Substring(0, $BaseName.Length - $have.Length) + $want
            }
        }
    }
    $BaseName
}

# Bill of materials for the drawing: one row per part of the type, sizes from the values.
# A split inner panel is listed as 2 halves.
function Get-BomInfo($Standard, [hashtable]$Values, [double]$Thickness, [string]$Name, $Layout) {
    $x = [string][char]0x00D7
    $W = [double]$Values['Width']; $L = [double]$Values['Length']; $H = [double]$Values['Height']
    $C = [double]$Values['Clearance']
    $m = $SizeRegex.Match($Name)
    $token = $m.Value
    $lead = ''
    if ($m.Success) {
        $before = $Name.Substring(0, $m.Index)   # part files carry the project lead, not the type prefix
        if ($before.EndsWith($Standard.Prefix, [StringComparison]::OrdinalIgnoreCase)) {
            $lead = $before.Substring(0, $before.Length - $Standard.Prefix.Length)
        }
    } else {
        $token = "$(Format-Number $W)x$(Format-Number $L)x$(Format-Number $H)"
    }
    $inset = 2 * $Thickness + 2 * $C
    $split = 1
    if ($Layout -and $Layout.Split) { $split = [int]$Layout.Split }

    $pos = 0
    $rows = @(foreach ($p in $Standard.Parts) {
        $pos++
        $suffix = $p.Suffix; $qty = $p.Qty
        $size = switch ($p.Role) {
            'outer' { "$(Format-Number $W) $x $(Format-Number $L) $x $(Format-Number $H)" }
            'inner' {
                if ($split -ge 2) { $qty = $p.Qty * $split; if ($p.SplitSuffix) { $suffix = $p.SplitSuffix } }
                "$(Format-Number (($W - $inset) / $split)) $x $(Format-Number ($L - $inset)) $x $(Format-Number ($H - $Thickness))"
            }
            default { '' }
        }
        [pscustomobject]@{ Pos = "$pos"; Part = "$lead$token$suffix"; Desc = $p.Description; Qty = "$qty"; Size = $size }
    })

    $note = ''
    if ($Layout) {
        $nL = [int][math]::Round($Layout.Length.Holes); $nW = [int][math]::Round($Layout.Width.Holes)
        $dia = [string][char]0x00D8
        $note = "Holes $dia$(Format-Number ([double]$Values['HoleDia'])) per tray: 2 $x $nL (length walls) + 2 $x $nW (width walls) = $(2 * $nL + 2 * $nW)"
    }
    @{ Title = "Bill of materials - $Name ($($Standard.Name))"; Rows = $rows; Note = $note }
}
