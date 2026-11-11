# Shared helpers: logging, error formatting, number formatting, settings.

$Invariant = [Globalization.CultureInfo]::InvariantCulture

# Parameters encoded in the file name, in order: [project ]<type>-<Width>x<Length>x<Height>
$SizeParams = @('Width', 'Length', 'Height')
$SizeRegex  = [regex]'(\d+(?:\.\d+)?)x(\d+(?:\.\d+)?)x(\d+(?:\.\d+)?)'

# Error text with the script line it came from, plus the call chain, so failures can be traced.
function Format-Error($err) {
    $msg = "$($err.Exception.Message) [line $($err.InvocationInfo.ScriptLineNumber): $($err.InvocationInfo.Line.Trim())]"
    $stack = ($err.ScriptStackTrace -split "`r?`n" | ForEach-Object { "      $_" }) -join [Environment]::NewLine
    if ($stack) { $msg += [Environment]::NewLine + $stack }
    $msg
}

# Writes to the window's log box once it exists ($LogBox, set by InventorSizer.ps1), else to the console.
function Write-Log([string]$Message) {
    $line = '{0:HH:mm:ss}  {1}' -f (Get-Date), $Message
    if ($LogBox) { $LogBox.AppendText($line + [Environment]::NewLine) }
    else { Write-Host $line }
}

function Format-Number([double]$v) { $v.ToString('0.###', $Invariant) }

# ------------------------------------------------------------------ settings.json

# The folder for new sizes is deliberately not a saved setting: new files always go next to
# the originals unless another folder is chosen in the window for that session.
$DefaultSettings = [ordered]@{
    minHoleGap   = 10    # mm between hole centres before Apply is blocked
    thickness    = 1     # mm, sheet thickness (Staerke) used for the centre-pair offset
    splitWidth   = 500   # mm, panels wider than this get the inner panel (-In) split in 2; 0 = never
    maxHolePitch = 400   # mm, automatic hole counts: largest gap between wall holes; 0 = enter the counts by hand
}

function Read-Settings([string]$Path) {
    $s = [ordered]@{}
    foreach ($k in $DefaultSettings.Keys) { $s[$k] = $DefaultSettings[$k] }
    if (Test-Path $Path) {
        $json = Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($k in $DefaultSettings.Keys) {
            if ($json.PSObject.Properties.Name -contains $k) { $s[$k] = $json.$k }
        }
    }
    $s.minHoleGap   = [double]$s.minHoleGap
    $s.thickness    = [double]$s.thickness
    $s.splitWidth   = [double]$s.splitWidth
    $s.maxHolePitch = [double]$s.maxHolePitch
    $s
}

function Save-Settings([string]$Path, $Incoming) {
    $s = Read-Settings $Path
    foreach ($k in $DefaultSettings.Keys) {
        if ($Incoming.PSObject.Properties.Name -contains $k) { $s[$k] = $Incoming.$k }
    }
    $s.minHoleGap   = [double]$s.minHoleGap
    $s.thickness    = [double]$s.thickness
    $s.splitWidth   = [double]$s.splitWidth
    $s.maxHolePitch = [double]$s.maxHolePitch
    if ($s.maxHolePitch -lt 0) { throw 'The maximum hole pitch cannot be negative.' }
    if ($s.splitWidth -lt 0) { throw 'The split width cannot be negative.' }
    if ($s.minHoleGap -lt 0) { throw 'Minimum hole gap cannot be negative.' }
    if ($s.thickness -le 0) { throw 'Sheet thickness must be greater than 0.' }
    $s | ConvertTo-Json | Set-Content $Path -Encoding UTF8
    Write-Log "Settings saved: min hole gap $(Format-Number $s.minHoleGap) mm, thickness $(Format-Number $s.thickness) mm, split above $(Format-Number $s.splitWidth) mm"
    $s
}
