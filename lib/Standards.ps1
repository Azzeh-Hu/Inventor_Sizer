# Product standards: one JSON file per family in .\standards (e.g. FP.json), each with one or
# more variants (FP with 1 inner panel, 2 inner panels, ...). A variant sets the name prefix,
# the master assembly new sizes start from, and the parts listed in the bill of materials.

function New-StandardPart([string]$Suffix, [string]$Description, [string]$Role, [int]$Qty) {
    [pscustomobject]@{ Suffix = $Suffix; Description = $Description; Role = $Role; Qty = $Qty }
}

# Used when no standards file can be read, so the app still works.
$BuiltInStandard = [pscustomobject]@{
    Id = 'FP-1In'; Name = 'FP with 1 inner panel'; Family = 'FP'; Prefix = 'FP-'; Master = ''; File = '(built in)'
    Parts = @((New-StandardPart '-Au' 'Outer tray (Au)' 'outer' 1), (New-StandardPart '-In' 'Inner panel (In)' 'inner' 1))
}

function Read-Standards([string]$Dir, [string]$Root) {
    $list = New-Object System.Collections.Generic.List[object]
    if (Test-Path $Dir) {
        foreach ($file in Get-ChildItem $Dir -Filter '*.json' | Sort-Object Name) {
            try { $json = Get-Content $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json }
            catch { Write-Log "Standard $($file.Name) could not be read: $($_.Exception.Message)"; continue }
            foreach ($v in @($json.variants)) {
                if ($v.PSObject.Properties.Name -contains 'enabled' -and -not $v.enabled) { continue }
                if (-not $v.id -or -not $v.prefix -or -not $v.parts) {
                    Write-Log "Standard $($file.Name): a type is missing id, prefix or parts - skipped"
                    continue
                }
                $master = [string]$v.master
                if ($master -and -not [IO.Path]::IsPathRooted($master)) { $master = Join-Path $Root $master }
                $list.Add([pscustomobject]@{
                    Id = [string]$v.id; Name = [string]$v.name; Family = [string]$json.family
                    Prefix = [string]$v.prefix; Master = $master; File = $file.Name
                    Parts = @(foreach ($p in $v.parts) {
                        New-StandardPart ([string]$p.suffix) ([string]$p.description) ([string]$p.role) ([int]$p.qty) })
                })
            }
        }
    }
    if ($list.Count -eq 0) {
        $list.Add($BuiltInStandard)
        Write-Log 'No standards found in .\standards - using the built-in FP standard'
    }
    , $list.ToArray()
}

# The type whose prefix matches the start of the name (longest prefix wins).
function Find-StandardForName($Standards, [string]$Name) {
    $best = $null
    foreach ($s in $Standards) {
        if ($Name.StartsWith($s.Prefix, [StringComparison]::OrdinalIgnoreCase) -and
            ($null -eq $best -or $s.Prefix.Length -gt $best.Prefix.Length)) { $best = $s }
    }
    $best
}

# Bill of materials for the drawing: one row per part of the type, sizes from the values.
function Get-BomInfo($Standard, [hashtable]$Values, [double]$Thickness, [string]$Name, $Layout) {
    $x = [string][char]0x00D7
    $W = [double]$Values['Width']; $L = [double]$Values['Length']; $H = [double]$Values['Height']
    $C = [double]$Values['Clearance']
    $token = $SizeRegex.Match($Name).Value
    if (-not $token) { $token = "$(Format-Number $W)x$(Format-Number $L)x$(Format-Number $H)" }
    $inset = 2 * $Thickness + 2 * $C

    $pos = 0
    $rows = @(foreach ($p in $Standard.Parts) {
        $pos++
        $size = switch ($p.Role) {
            'outer' { "$(Format-Number $W) $x $(Format-Number $L) $x $(Format-Number $H)" }
            'inner' { "$(Format-Number ($W - $inset)) $x $(Format-Number ($L - $inset)) $x $(Format-Number ($H - $Thickness))" }
            default { '' }
        }
        [pscustomobject]@{ Pos = "$pos"; Part = "$token$($p.Suffix)"; Desc = $p.Description; Qty = "$($p.Qty)"; Size = $size }
    })

    $note = ''
    if ($Layout) {
        $nL = [int][math]::Round($Layout.Length.Holes); $nW = [int][math]::Round($Layout.Width.Holes)
        $dia = [string][char]0x00D8
        $note = "Holes $dia$(Format-Number ([double]$Values['HoleDia'])) per tray: 2 $x $nL (length walls) + 2 $x $nW (width walls) = $(2 * $nL + 2 * $nW)"
    }
    @{ Title = "Bill of materials - $Name ($($Standard.Name))"; Rows = $rows; Note = $note }
}
