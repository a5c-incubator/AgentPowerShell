param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $repoRoot

$testArgs = @("test", "agentpowershell.sln", "--verbosity", "minimal")
if ($NoBuild) {
    $testArgs += "--no-build"
}

dotnet build agentpowershell.sln --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet @testArgs
exit $LASTEXITCODE
