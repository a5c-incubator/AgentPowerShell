using AgentPowerShell.Daemon;
using AgentPowerShell.Core;
using AgentPowerShell.Protos;
using AgentPowerShell.Shim;
using Xunit;

namespace AgentPowerShell.IntegrationTests;

public sealed class SmokeTests
{
    [Fact]
    public void Placeholder_Integration_Test()
    {
        Assert.True(true);
    }

    [Fact]
    public async Task ShimProcessor_Runs_PowerShell_Command()
    {
        var shell = RealPowerShellResolver.Resolve("pwsh");
        var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-sessions.json");
        var store = new SessionStore(storePath);
        await store.LoadAsync(CancellationToken.None);
        var processor = new ShimCommandProcessor(store, new AgentPowerShellConfig());
        var response = await processor.ExecuteAsync(
            new ShimCommandRequest
            {
                ExecutablePath = shell,
                WorkingDirectory = Environment.CurrentDirectory,
                Arguments = ["-NoLogo", "-NoProfile", "-Command", "Write-Output 'shim-ok'"]
            },
            CancellationToken.None);

        Assert.Equal(0, response.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(response.SessionId));
        Assert.Contains("shim-ok", response.Stdout, StringComparison.OrdinalIgnoreCase);
    }
}
