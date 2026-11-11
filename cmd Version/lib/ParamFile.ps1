# Parameters.xml: the input values, in the layout of Inventor's Parameters dialog "Export to XML",
# so the file can also be imported there by hand. Formulas live in Inventor, not here.

$DefaultParams = @(
    @('Width',     '386', 'mm', '1st number in the file name'),
    @('Length',    '656', 'mm', '2nd number in the file name'),
    @('Height',    '47',  'mm', '3rd number in the file name'),
    @('HolesL',    '3',   'ul', 'Holes per length wall (0, 1, 2, 3, ...)'),
    @('HolesW',    '2',   'ul', 'Holes per width wall (0, 1, 2, 4, 6, ...)'),
    @('HoleEdge',  '50',  'mm', 'Hole centre distance from the panel edge (-Au)'),
    @('HoleZ',     '22',  'mm', 'Hole centre height'),
    @('HoleDia',   '4.1', 'mm', 'Hole diameter (fixed)'),
    @('Clearance', '0.5', 'mm', 'Gap between -In and the -Au wall (assembly offsets)'),
    @('HingeCount',  '2', 'ul', 'TP: hinges per door, evenly spaced (other types: not used)'),
    @('HandleCount', '2', 'ul', 'TP: handles per door, evenly spaced (other types: not used)')
)
$ValueRegex = [regex]'^\s*(-?\d+(?:[.,]\d+)?)\s*([^\d\s]*)\s*$'

function New-Param([string]$Name, [double]$Value, [string]$Unit, [string]$Description = '') {
    [pscustomobject]@{ Name = $Name; Value = $Value; Unit = $Unit; Description = $Description }
}

function Format-Value($p) { ("{0} {1}" -f (Format-Number $p.Value), $p.Unit).Trim() }

function Save-ParamFile([string]$Path, [object[]]$Params) {
    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $w = [System.Xml.XmlWriter]::Create($Path, $settings)
    try {
        $w.WriteStartDocument()
        $w.WriteStartElement('ParamWithValueList')
        $w.WriteAttributeString('xmlns', 'xsd', $null, 'http://www.w3.org/2001/XMLSchema')
        $w.WriteAttributeString('xmlns', 'xsi', $null, 'http://www.w3.org/2001/XMLSchema-instance')
        $w.WriteElementString('version', '20080502')
        $w.WriteStartElement('parameterTypes')
        foreach ($t in @(@('Inventor', '0'), @('String', '1'), @('Boolean', '2'))) {
            $w.WriteStartElement('ParamType')
            $w.WriteElementString('typeName', $t[0])
            $w.WriteElementString('typeCode', $t[1])
            $w.WriteEndElement()
        }
        $w.WriteEndElement()
        $w.WriteStartElement('parameters')
        foreach ($p in $Params) {
            $w.WriteStartElement('ParamWithValue')
            $w.WriteElementString('name', $p.Name)
            $w.WriteElementString('typeCode', $(if ($p.Unit) { $p.Unit } else { 'ul' }))
            $w.WriteElementString('value', (Format-Value $p))
            $w.WriteElementString('comment', [string]$p.Description)
            $w.WriteElementString('isKey', 'false')
            $w.WriteEndElement()
        }
        $w.WriteEndElement()
        $w.WriteEndElement()
        $w.WriteEndDocument()
    } finally {
        $w.Close()
    }
}

# Reads the file, creating it with defaults if missing. Default entries missing from an older
# file (e.g. HolesL / HolesW) are added, so existing files keep working after an update.
function Read-ParamFile([string]$Path) {
    $list = New-Object System.Collections.Generic.List[object]
    if (Test-Path $Path) {
        $xml = New-Object System.Xml.XmlDocument
        $xml.Load($Path)
        foreach ($node in $xml.SelectNodes('//ParamWithValue')) {
            $name = $node.SelectSingleNode('name').InnerText.Trim()
            $value = $node.SelectSingleNode('value').InnerText
            $m = $ValueRegex.Match($value)
            if (-not $m.Success) {
                throw "Parameters.xml: $name = '$value' is not a number with a unit. This file holds input values only; put formulas in Inventor."
            }
            $comment = $node.SelectSingleNode('comment')
            $list.Add((New-Param $name ([double]::Parse($m.Groups[1].Value.Replace(',', '.'), $Invariant)) `
                        $m.Groups[2].Value $(if ($comment) { $comment.InnerText } else { '' })))
        }
    }
    $added = @()
    foreach ($d in $DefaultParams) {
        if (-not ($list | Where-Object { $_.Name -eq $d[0] })) {
            $list.Add((New-Param $d[0] ([double]::Parse($d[1], $Invariant)) $d[2] $d[3]))
            $added += $d[0]
        }
    }
    if ($added) {
        Save-ParamFile $Path $list.ToArray()
        Write-Log "Parameters.xml: added $($added -join ', ')"
    }
    , $list.ToArray()
}
