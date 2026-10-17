function Get-RecorderCompiler([string]$CompilerPath) {
    if ($CompilerPath) {
        $resolved = [IO.Path]::GetFullPath($CompilerPath)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw 'Compiler DLL not found.' }
        return $resolved
    }
    $sdks = @(& dotnet --list-sdks)
    if ($LASTEXITCODE -ne 0) { throw 'Install a .NET SDK or specify -CompilerPath.' }
    for ($index = $sdks.Count - 1; $index -ge 0; $index--) {
        if ($sdks[$index] -match '^(\S+) \[(.+)\]$') {
            $candidate = Join-Path $Matches[2] ($Matches[1] + '/Roslyn/bincore/csc.dll')
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
        }
    }
    throw 'No Roslyn csc.dll found in installed .NET SDKs.'
}
