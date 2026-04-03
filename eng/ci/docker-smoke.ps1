param(
    [string]$ImageTag = "agentpowershell:ci"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $repoRoot

docker build -t $ImageTag .
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

docker run --rm $ImageTag version
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

docker run --rm $ImageTag policy validate /app/default-policy.yml --output json
exit $LASTEXITCODE
