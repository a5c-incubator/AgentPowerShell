param(
    [string]$Destination = "$HOME\\.local\\bin",
    [string]$Configuration = "Release",
    [string]$Rid,
    [switch]$SelfContained = $true,
    [switch]$SkipUserEnvironmentUpdate
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

if (-not $Rid) {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        if ($architecture -eq "Arm64") {
            $Rid = "win-arm64"
        } else {
            $Rid = "win-x64"
        }
    } elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        if ($architecture -eq "Arm64") {
            $Rid = "osx-arm64"
        } else {
            $Rid = "osx-x64"
        }
    } else {
        if ($architecture -eq "Arm64") {
            $Rid = "linux-arm64"
        } else {
            $Rid = "linux-x64"
        }
    }
}

$dotnet = $env:DOTNET_HOST_PATH
if (-not $dotnet -or -not (Test-Path $dotnet)) {
    $dotnet = Join-Path $HOME ".dotnet\dotnet.exe"
}
if (-not $dotnet -or -not (Test-Path $dotnet)) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetCommand) {
        $dotnet = $dotnetCommand.Source
    }
}
if (-not (Test-Path $dotnet)) {
    throw "Unable to locate dotnet. Install the .NET 9 SDK or set DOTNET_HOST_PATH."
}

$publishRoot = Join-Path $repoRoot ".artifacts\install\$Rid"
$cliOut = Join-Path $publishRoot "cli"
$daemonOut = Join-Path $publishRoot "daemon"

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
Remove-Item -Recurse -Force $publishRoot -ErrorAction SilentlyContinue

if (-not $SelfContained) {
    throw "Non-self-contained install publishes are not supported by the current solution layout. Use the default self-contained install."
}

$commonArgs = @("-c", $Configuration, "-r", $Rid, "-p:PublishSingleFile=true")
$commonArgs += "--self-contained"
$commonArgs += "true"

& $dotnet publish "src/AgentPowerShell.Cli/AgentPowerShell.Cli.csproj" @commonArgs "-o" $cliOut
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $dotnet publish "src/AgentPowerShell.Daemon/AgentPowerShell.Daemon.csproj" @commonArgs "-o" $daemonOut
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Copy-Item (Join-Path $cliOut "*") -Destination $Destination -Recurse -Force
Copy-Item default-policy.yml -Destination (Join-Path $Destination "default-policy.yml") -Force
Copy-Item config.yml -Destination (Join-Path $Destination "config.yml") -Force

$daemonTarget = Join-Path $Destination "daemon"
New-Item -ItemType Directory -Force -Path $daemonTarget | Out-Null
Copy-Item (Join-Path $daemonOut "*") -Destination $daemonTarget -Recurse -Force

$daemonBinaryName = if ($Rid.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) { "AgentPowerShell.Daemon.exe" } else { "AgentPowerShell.Daemon" }
$daemonBinary = Join-Path $daemonTarget $daemonBinaryName
if ((Test-Path $daemonBinary) -and -not $SkipUserEnvironmentUpdate) {
    [Environment]::SetEnvironmentVariable("AGENTPOWERSHELL_DAEMON_PATH", $daemonBinary, "User")
}

Write-Host "Installed AgentPowerShell to $Destination"
Write-Host "RID: $Rid"
if ((Test-Path $daemonBinary) -and -not $SkipUserEnvironmentUpdate) {
    Write-Host "Configured AGENTPOWERSHELL_DAEMON_PATH for the current user."
} elseif ((Test-Path $daemonBinary) -and $SkipUserEnvironmentUpdate) {
    Write-Host "Skipped AGENTPOWERSHELL_DAEMON_PATH user-environment update."
}
