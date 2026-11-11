<#
.SYNOPSIS
    Builds InventorSizer.exe (.NET Framework 4.8, WinForms) with the Roslyn compiler that ships
    with PowerShell 7, and runs the Core tests. No Visual Studio or .NET SDK needed.

    pwsh tools/build.ps1 -RefDir <net48 reference assemblies> [-Test] [-TestData <dir>]

    -RefDir   folder with the .NET Framework 4.8 reference assemblies (mscorlib.dll, ...), e.g.
              the "build/.NETFramework/v4.8" folder of the NuGet package
              microsoft.netframework.referenceassemblies.net48. On Windows with Visual Studio
              or the Developer Pack: C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8
    -Test     also compile src/Core + tests against the running .NET and run the tests
    -TestData folder with the GR export files used by the tests (optional)

    Visual Studio / dotnet users can build src\InventorSizer.csproj instead.
#>
param([string]$RefDir = '', [switch]$Test, [string]$TestData = '', [switch]$NoApp)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$src = Join-Path $root 'src'
Add-Type -AssemblyName Microsoft.CodeAnalysis, Microsoft.CodeAnalysis.CSharp

function Get-Trees([string[]]$Files) {
    $opt = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::new([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp10)
    foreach ($f in $Files) { [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($f), $opt, $f, [Text.Encoding]::UTF8) }
}

function Invoke-Compile([string]$Name, [string[]]$Files, $Refs, $Kind, [string]$Out) {
    $opts = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new($Kind)
    $opts = $opts.WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithPlatform([Microsoft.CodeAnalysis.Platform]::AnyCpu)
    $comp = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create($Name, [Microsoft.CodeAnalysis.SyntaxTree[]]@(Get-Trees $Files), [Microsoft.CodeAnalysis.MetadataReference[]]$Refs, $opts)
    $ms = [IO.MemoryStream]::new()
    $res = $comp.Emit($ms)
    $diag = @($res.Diagnostics | Where-Object { $_.Severity -ge [Microsoft.CodeAnalysis.DiagnosticSeverity]::Warning -and $_.Id -notin 'CS1701', 'CS1702', 'CS8019' })
    foreach ($d in $diag) { Write-Host $d.ToString() -ForegroundColor $(if ($d.Severity -eq 'Error') { 'Red' } else { 'DarkYellow' }) }
    if (-not $res.Success) { throw "$Name : build failed" }
    if ($Out) { [IO.File]::WriteAllBytes($Out, $ms.ToArray()); Write-Host "built $Out ($([math]::Round($ms.Length / 1KB)) KB)" }
    , $ms.ToArray()
}

$core = @(Get-ChildItem (Join-Path $src 'Core') -Recurse -Filter *.cs | ForEach-Object FullName)

if ($Test) {
    $tpa = ([AppContext]::GetData('TRUSTED_PLATFORM_ASSEMBLIES') -split [IO.Path]::PathSeparator) | Where-Object { $_ -match '\.dll$' }
    $refs = @(foreach ($p in $tpa) { try { [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($p) } catch { } })
    $tests = @(Get-ChildItem (Join-Path $root 'tests') -Filter *.cs | ForEach-Object FullName)
    $bytes = Invoke-Compile 'InventorSizer.Tests' ($core + $tests) $refs ([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary) ''
    $asm = [Reflection.Assembly]::Load([byte[]]$bytes)
    $code = $asm.GetType('InventorSizer.Tests.Program').GetMethod('Run').Invoke($null, [object[]]@(, [string[]]@($root, $TestData)))
    if ($code -ne 0) { throw "$code test(s) failed" }
}

if (-not $NoApp) {
    if (-not $RefDir -or -not (Test-Path (Join-Path $RefDir 'mscorlib.dll'))) { throw 'Give -RefDir: the folder with the .NET Framework 4.8 reference assemblies.' }
    $names = 'mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Windows.Forms', 'System.Xml', 'System.Data', 'Microsoft.CSharp', 'Microsoft.VisualBasic', 'System.Numerics'
    $refs = @(foreach ($n in $names) { [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $RefDir "$n.dll")) })
    $facades = Join-Path $RefDir 'Facades'
    if (Test-Path $facades) { $refs += @(Get-ChildItem $facades -Filter *.dll | ForEach-Object { [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_.FullName) }) }
    $app = @(Get-ChildItem (Join-Path $src 'App') -Recurse -Filter *.cs | ForEach-Object FullName)
    [void](Invoke-Compile 'InventorSizer' ($core + $app) $refs ([Microsoft.CodeAnalysis.OutputKind]::WindowsApplication) (Join-Path $root 'InventorSizer.exe'))
}
