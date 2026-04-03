param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$destination = Join-Path ([System.IO.Path]::GetTempPath()) ("agentpowershell-install-" + [guid]::NewGuid().ToString("N"))

try {
    Set-Location $repoRoot
    ./install.ps1 -Destination $destination -Configuration $Configuration -SkipUserEnvironmentUpdate

    $cli = @(
        Join-Path $destination "AgentPowerShell.Cli.exe"
        Join-Path $destination "AgentPowerShell.Cli"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    $daemon = @(
        Join-Path $destination "daemon\\AgentPowerShell.Daemon.exe"
        Join-Path $destination "daemon\\AgentPowerShell.Daemon"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $cli) {
        throw "Expected installed CLI under $destination"
    }

    if (-not $daemon) {
        throw "Expected installed daemon under $(Join-Path $destination 'daemon')"
    }

    $env:AGENTPOWERSHELL_DAEMON_PATH = $daemon

    & $cli version
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $cli policy validate (Join-Path $destination "default-policy.yml") --output json
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $cli start --output json
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $cli status --output json
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    & $cli stop --output json
    exit $LASTEXITCODE
}
finally {
    Remove-Item -Recurse -Force $destination -ErrorAction SilentlyContinue
}
