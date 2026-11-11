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
    Write-Dev "log: $Message"
}

# ------------------------------------------------------------------ dev log
#
# Everything the app does, with the time since start (ms) and how long each step took, so
# slow or failing steps can be found. Shown in the "Dev log" tab and saved to devlog.txt.

$DevWatch = [Diagnostics.Stopwatch]::StartNew()
$DevLines = New-Object System.Collections.Generic.List[string]
$DevDepth = 0

function Write-Dev([string]$Message) {
    $line = ('{0,9:N0} ms  {1}{2}' -f $DevWatch.ElapsedMilliseconds, ('  ' * $script:DevDepth), $Message)
    $DevLines.Add($line)
    if ($DevLogBox) { $DevLogBox.AppendText($line + [Environment]::NewLine) }
}

# Runs a step and writes its start, end and duration. Returns what the step returns.
function Measure-Dev([string]$Label, [scriptblock]$Body) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    Write-Dev "> $Label"
    $script:DevDepth++
    try { & $Body }
    catch { Write-Dev "! $Label failed after $($sw.ElapsedMilliseconds) ms: $($_.Exception.Message)"; throw }
    finally { $script:DevDepth = [math]::Max(0, $script:DevDepth - 1); Write-Dev "< $Label  $($sw.ElapsedMilliseconds) ms" }
}

function Save-DevLog([string]$Path) { [IO.File]::WriteAllLines($Path, $DevLines.ToArray(), (New-Object System.Text.UTF8Encoding($false))) }

# ------------------------------------------------------------------ COM helpers

# A value that may not exist (missing property, suppressed part): $Default instead of an error.
function Get-Safe([scriptblock]$Get, $Default = '') { try { $v = & $Get; if ($null -eq $v) { $Default } else { $v } } catch { $Default } }

function Get-ComTypeName($Object) {
    if ($null -eq $Object) { return '' }
    try { [Microsoft.VisualBasic.Information]::TypeName($Object) } catch { $Object.GetType().Name }
}

# The items of a COM collection. Some collections (SubOccurrences of a sub-assembly) report a
# Count that Item(i) cannot reach, so the collection is enumerated directly, with Item(i) as a
# fallback that skips what Inventor cannot return.
function Get-OccurrenceList($Collection) {
    $list = New-Object System.Collections.Generic.List[object]
    $enum = $null
    try { $enum = [System.Management.Automation.LanguagePrimitives]::GetEnumerable($Collection) } catch { }
    if ($null -ne $enum) {
        try { foreach ($o in $enum) { if ($null -ne $o) { $list.Add($o) } } } catch { $list.Clear() }
    }
    if ($list.Count -eq 0) {
        $n = Get-Safe { $Collection.Count } 0
        for ($i = 1; $i -le $n; $i++) {
            try { $o = $Collection.Item($i); if ($null -ne $o) { $list.Add($o) } } catch { }
        }
    }
    , $list.ToArray()
}

# CSV with the Windows list separator and decimal sign, so Excel opens it directly.
function Export-CsvLocal($Rows, [string]$Path) {
    $sep = (Get-Culture).TextInfo.ListSeparator
    $culture = Get-Culture
    $out = foreach ($r in $Rows) {
        $o = [ordered]@{}
        foreach ($p in $r.PSObject.Properties) { $o[$p.Name] = $(if ($p.Value -is [double]) { $p.Value.ToString('0.###', $culture) } else { $p.Value }) }
        [pscustomobject]$o
    }
    $out | Export-Csv -Path $Path -NoTypeInformation -Delimiter $sep -Encoding UTF8
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
