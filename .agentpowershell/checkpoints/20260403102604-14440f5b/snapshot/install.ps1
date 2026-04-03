param(
    [string]$Destination = "$HOME\\.local\\bin"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

Write-Host "Publish AgentPowerShell locally before installation:"
Write-Host "dotnet publish src/AgentPowerShell.Cli/AgentPowerShell.Cli.csproj -c Release -p:PublishProfile=win-x64"
Write-Host "Copy the published files into $Destination."
