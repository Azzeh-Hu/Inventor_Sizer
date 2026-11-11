# Everything that talks to Autodesk Inventor through COM.

$kPartDocumentObject     = 12290
$kAssemblyDocumentObject = 12291
$DesignTrackingProps     = '{32853F0F-3444-11D1-9E93-0060B03C1CA6}'   # internal name, same in every language
$PartNumberPropId        = 5

function Get-InventorApp {
    try { [Runtime.InteropServices.Marshal]::GetActiveObject('Inventor.Application') }
    catch {
        throw ('Autodesk Inventor is not running. If it is open, Inventor and this app must run at the same ' +
               'permission level (both normal, or both "Run as administrator").')
    }
}

# Status for the UI; never throws.
function Get-InventorInfo {
    try {
        $inv = [Runtime.InteropServices.Marshal]::GetActiveObject('Inventor.Application')
        $doc = $inv.ActiveDocument
        if ($null -eq $doc) { return @{ connected = $true; document = ''; isAssembly = $false } }
        @{
            connected  = $true
            document   = [IO.Path]::GetFileNameWithoutExtension($doc.FullFileName)
            isAssembly = ($doc.DocumentType -eq $kAssemblyDocumentObject)
        }
    } catch {
        @{ connected = $false; document = ''; isAssembly = $false }
    }
}

function Get-ActiveAssembly {
    $doc = (Get-InventorApp).ActiveDocument
    if ($null -eq $doc -or $doc.DocumentType -ne $kAssemblyDocumentObject) {
        throw 'Open the assembly (.iam) in Inventor and make it the active window.'
    }
    $doc
}

# The assembly itself plus every part / sub-assembly it uses, at any depth.
function Get-ModelDocuments($asm) {
    $docs = New-Object System.Collections.Generic.List[object]
    $docs.Add($asm)
    $refs = $asm.AllReferencedDocuments
    for ($i = 1; $i -le $refs.Count; $i++) {
        $d = $refs.Item($i)
        if ($d.DocumentType -eq $kPartDocumentObject -or $d.DocumentType -eq $kAssemblyDocumentObject) {
            $docs.Add($d)
        }
    }
    , $docs.ToArray()
}

function Get-UnitKind([string]$Unit) {
    switch ($Unit.Trim().ToLowerInvariant()) {
        { $_ -in 'mm', 'cm', 'm', 'in', 'ft' } { return 'length' }
        { $_ -in '', 'ul' }                    { return 'unitless' }
        { $_ -in 'deg', 'rad' }                { return 'angle' }
        default                                { return $_ }
    }
}

# Inventor stores values internally in cm / rad. Setting Parameter.Value with these avoids
# any decimal-comma / decimal-point problem with the Windows language setting.
function ConvertTo-DbValue([double]$Value, [string]$Unit) {
    switch ($Unit.Trim().ToLowerInvariant()) {
        'mm'    { return $Value / 10 }
        'cm'    { return $Value }
        'm'     { return $Value * 100 }
        'in'    { return $Value * 2.54 }
        'ft'    { return $Value * 30.48 }
        'deg'   { return $Value * [math]::PI / 180 }
        default { return $Value }
    }
}

function Push-Parameters($asm, [object[]]$Params) {
    $byName = @{}
    foreach ($p in $Params) { $byName[$p.Name] = $p }
    $used = @{}
    $changed = 0

    foreach ($doc in (Get-ModelDocuments $asm)) {
        if (-not $doc.IsModifiable) { continue }   # library / Content Center / read-only files
        $short = [IO.Path]::GetFileName($doc.FullFileName)
        $plist = $doc.ComponentDefinition.Parameters
        for ($i = 1; $i -le $plist.Count; $i++) {
            $ip = $plist.Item($i)
            if (-not $byName.ContainsKey($ip.Name)) { continue }
            $used[$ip.Name] = $true
            $src = $byName[$ip.Name]
            if ((Get-UnitKind $src.Unit) -ne (Get-UnitKind ([string]$ip.Units))) {
                Write-Log "  $short : $($ip.Name) is '$($ip.Units)' in Inventor but '$($src.Unit)' in the app - skipped"
                continue
            }
            $target = ConvertTo-DbValue $src.Value $src.Unit
            if ([math]::Abs($ip.Value - $target) -lt 1e-9) { continue }   # compare numbers, not text
            try {
                $old = $ip.Expression
                $ip.Value = $target
                $changed++
                Write-Log "  $short : $($ip.Name)  $old -> $(Format-Value $src)"
            } catch {
                Write-Log "  $short : could not set $($ip.Name) ($(Format-Error $_))"
            }
        }
    }

    foreach ($p in $Params) {
        if (-not $used.ContainsKey($p.Name)) { Write-Log "  note: no Inventor parameter named '$($p.Name)' was found" }
    }
    if ($changed -eq 0) { Write-Log '  no parameter values changed' }

    # Rebuild parts first, then the assembly so constraints re-solve against the new geometry.
    $docs = Get-ModelDocuments $asm
    for ($i = $docs.Count - 1; $i -ge 0; $i--) {
        if ($docs[$i].IsModifiable) { $docs[$i].Update2($true) }
    }
}

# Swap the old size for the new one in the Part Number iProperty (BOMs and drawings use it).
function Update-PartNumber($Doc, [string]$OldToken, [string]$NewToken) {
    $short = [IO.Path]::GetFileName($Doc.FullFileName)
    try {
        $prop = $Doc.PropertySets.Item($DesignTrackingProps).ItemByPropId($PartNumberPropId)
        $old = [string]$prop.Value
        if ($old.Contains($OldToken)) {
            $prop.Value = $old.Replace($OldToken, $NewToken)
            Write-Log "  $short : Part Number $old -> $($prop.Value)"
        } else {
            Write-Log "  $short : Part Number '$old' has no size in it - left as is"
        }
    } catch {
        Write-Log "  $short : could not update the Part Number ($(Format-Error $_))"
    }
}

# Save the assembly and every file whose name contains $OldToken under the new size,
# into $OutputFolder (or next to the originals when it is empty).
function Save-AsNewSize($asm, [string]$OldToken, [string]$NewToken, [string]$NewAsmName, [string]$OutputFolder) {
    if ($OutputFolder -and -not (Test-Path $OutputFolder)) {
        [void](New-Item -ItemType Directory -Path $OutputFolder)
        Write-Log "  created folder $OutputFolder"
    }
    $plan = New-Object System.Collections.Generic.List[object]
    foreach ($doc in (Get-ModelDocuments $asm)) {
        if ($doc.FullFileName -eq $asm.FullFileName -or -not $doc.IsModifiable) { continue }
        $name = [IO.Path]::GetFileName($doc.FullFileName)
        if ($name.Contains($OldToken)) {
            $dir = $(if ($OutputFolder) { $OutputFolder } else { Split-Path $doc.FullFileName })
            $plan.Add(@{ Doc = $doc; Path = (Join-Path $dir ($name.Replace($OldToken, $NewToken))) })
        }
    }
    $asmDir = $(if ($OutputFolder) { $OutputFolder } else { Split-Path $asm.FullFileName })
    $plan.Add(@{ Doc = $asm; Path = (Join-Path $asmDir ($NewAsmName + [IO.Path]::GetExtension($asm.FullFileName))) })

    $existing = @($plan | Where-Object { Test-Path $_.Path } | ForEach-Object { $_.Path })
    if ($existing) { throw "These files already exist, nothing was saved:`n" + ($existing -join "`n") }

    $inv = $asm.Parent
    $inv.SilentOperation = $true
    try {
        # SaveAs (not copy) re-points the open assembly at the new files; the originals on disk stay as they were.
        foreach ($item in $plan) {
            Update-PartNumber $item.Doc $OldToken $NewToken
            $item.Doc.SaveAs($item.Path, $false)
            Write-Log "  saved $($item.Path)"
        }
        $asm.Save2($true)   # flush sub-assemblies whose references changed
    } finally {
        $inv.SilentOperation = $false
    }
}

function Read-ActiveAssemblyName {
    $asm = Get-ActiveAssembly
    $name = [IO.Path]::GetFileNameWithoutExtension($asm.FullFileName)
    if (-not $SizeRegex.IsMatch($name)) { throw "'$name' does not contain a size like 386x656x47." }
    Write-Log "Active assembly: $($asm.FullFileName)"
    $name
}

function Invoke-Apply([string]$Name, [object[]]$Params, $Settings, [string]$ParamPath) {
    $newName = $Name.Trim()
    $m = $SizeRegex.Match($newName)
    if (-not $m.Success) { throw 'The name must contain a size like 386x656x47.' }
    if ($newName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) { throw "'$newName' is not a valid file name." }
    $newToken = $m.Value

    $values = @{}
    foreach ($p in $Params) { $values[$p.Name] = [double]$p.Value }
    for ($i = 0; $i -lt $SizeParams.Count; $i++) {
        $fromName = [double]::Parse($m.Groups[$i + 1].Value, $Invariant)
        if ($values[$SizeParams[$i]] -ne $fromName) {
            throw "The name says $($SizeParams[$i]) = $(Format-Number $fromName) but the value is $(Format-Number $values[$SizeParams[$i]])."
        }
    }

    $layout = Get-HoleLayout $values $Settings
    if ($layout.Errors.Count) { throw ("Not applied - fix these first:`n" + ($layout.Errors -join "`n")) }

    $asm = Get-ActiveAssembly
    $curName = [IO.Path]::GetFileNameWithoutExtension($asm.FullFileName)
    $curToken = $SizeRegex.Match($curName).Value
    if ($newName -ne $curName -and -not $curToken) {
        throw "The current file name '$curName' has no size in it, so the part files cannot be renamed."
    }

    Save-ParamFile $ParamPath $Params
    Write-Log "Saved values to $ParamPath"
    foreach ($p in $Params) { Write-Log "  $($p.Name) = $(Format-Value $p)" }
    Write-Log ("  holes: length wall {0} (pitch {1} mm), width wall {2}{3}" -f $layout.Length.Holes,
        (Format-Number $layout.Length.Pitch), $layout.Width.Holes, $(if ($layout.Width.MidOn) { ' incl. centre pair' } else { '' }))

    Write-Log "Updating $curName ..."
    Push-Parameters $asm (@($Params) + @($layout.Params))

    if ($newName -eq $curName) {
        $asm.Save2($true)
        Write-Log "Saved $curName in place."
    } else {
        Write-Log "Saving as $newName ..."
        Save-AsNewSize $asm $curToken $newToken $newName $Settings.outputFolder
        Write-Log "Now working on $newName. The $curName files on disk are unchanged."
    }
    $newName
}
