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
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

docker run --rm --entrypoint /bin/sh $ImageTag -lc "/app/AgentPowerShell.Cli start --output json && /app/AgentPowerShell.Cli status --output json && /app/AgentPowerShell.Cli stop --output json"
exit $LASTEXITCODE
