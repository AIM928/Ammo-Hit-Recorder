param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../dist'),
    [string]$CompilerPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Compiler.ps1')
$compiler = Get-RecorderCompiler $CompilerPath
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$managed = Join-Path ([IO.Path]::GetFullPath($GameRoot)) 'GHPC_Data/Managed'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$dll = Join-Path $output 'AIMAmmoHitRecorder.dll'
if (Test-Path -LiteralPath $dll) { throw 'Output DLL already exists; use a new output directory.' }
$references = @('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.TerrainPhysicsModule.dll') | ForEach-Object { Join-Path $managed $_ }
$references += Join-Path $GameRoot 'MelonLoader/net35/MelonLoader.dll'
$references += Join-Path $GameRoot 'MelonLoader/net35/0Harmony.dll'
foreach ($reference in $references) { if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) { throw "Missing existing game/loader reference: $reference" } }
$sources = @('Mod.cs','Records.cs','NativeObservation.cs','ShotAggregation.cs') | ForEach-Object { Join-Path $root ('src/' + $_) }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$arguments = @($compiler,'/nologo','/target:library','/langversion:latest',"/out:$dll") + @($references | ForEach-Object { "/reference:$_" }) + $sources
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Get-FileHash -LiteralPath $dll -Algorithm SHA256
Write-Output 'Build only: no files were installed into the game.'
