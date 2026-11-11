<#
.SYNOPSIS
    Installs the Assembly Reader add-in for the current Windows user: copies
    addin\bin\InventorSizer.Reader.* to %APPDATA%\Autodesk\ApplicationPlugins\InventorSizerReader.
    Build the add-in first (see addin\README.md). Close Inventor before running this.

    powershell -ExecutionPolicy Bypass -File tools\install-reader.ps1 [-Remove]
#>
param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$dest = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\InventorSizerReader'

if ($Remove) {
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force; Write-Host "Removed $dest" } else { Write-Host 'The add-in is not installed.' }
    return
}

$bin = Join-Path $root 'addin\bin'
if (-not (Test-Path (Join-Path $bin 'InventorSizer.Reader.dll'))) { throw "Build the add-in first: $bin\InventorSizer.Reader.dll is missing (see addin\README.md)." }
[void](New-Item -ItemType Directory -Force $dest)
try { Copy-Item (Join-Path $bin 'InventorSizer.Reader.*') $dest -Force }
catch { throw "Could not copy the add-in. Close Inventor and run this again. ($($_.Exception.Message))" }
Get-ChildItem $dest | Unblock-File
Write-Host "Installed to $dest"
Write-Host 'Start Inventor, open an assembly: the button "Assembly Reader" is on the Tools tab, panel "Sizer".'
