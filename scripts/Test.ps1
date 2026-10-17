param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../test-output'),
    [string]$CompilerPath,
    [string]$FrameworkDirectory = (Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Compiler.ps1')
$compiler = Get-RecorderCompiler $CompilerPath
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$exe = Join-Path $output 'NativeAdapter.exe'
if (Test-Path -LiteralPath $exe) { throw 'Test executable exists; use a new output directory.' }
$references = @('mscorlib.dll','System.dll','System.Core.dll') | ForEach-Object { Join-Path $FrameworkDirectory $_ }
foreach ($reference in $references) { if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) { throw "Missing .NET Framework reference: $reference" } }
$sources = @('tests/Stubs.cs','tests/NativeAdapter.cs','src/Mod.cs','src/Records.cs','src/NativeObservation.cs','src/ShotAggregation.cs') | ForEach-Object { Join-Path $root $_ }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$arguments = @($compiler,'/nologo','/target:exe','/langversion:latest',"/out:$exe") + @($references | ForEach-Object { "/reference:$_" }) + $sources
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Managed adapter tests failed.' }
Write-Output 'Managed stub tests only: this does not verify live Unity/Harmony safety.'
