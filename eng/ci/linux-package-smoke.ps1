param(
    [string]$Rid = "linux-x64",
    [string]$SdkImage = "mcr.microsoft.com/dotnet/sdk:9.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $repoRoot

$containerCommand = "chmod +x install.sh eng/ci/install-smoke.sh eng/ci/release-package-smoke.sh && ./eng/ci/install-smoke.sh && ./eng/ci/release-package-smoke.sh $Rid"
docker run --rm -v "${repoRoot}:/work" -w /work $SdkImage bash -lc $containerCommand
exit $LASTEXITCODE
