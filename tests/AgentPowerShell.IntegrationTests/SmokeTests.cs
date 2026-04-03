using AgentPowerShell.Daemon;
using AgentPowerShell.Core;
using AgentPowerShell.Cli;
using AgentPowerShell.Protos;
using AgentPowerShell.Shim;
using Xunit;

namespace AgentPowerShell.IntegrationTests;

public sealed class SmokeTests
{
    [Fact]
    public async Task Cli_Exec_Blocks_Explicit_Network_Target_And_Persists_Session()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-cli-network-integration");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;
        var originalOut = Console.Out;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "default-policy.yml"), """
                command_rules:
                  - name: allow-powershell
                    pattern: "powershell"
                    decision: allow
                network_rules:
                  - name: allow-nuget
                    domain: "api.nuget.org"
                    ports: ["443"]
                    decision: allow
                """);

            using var writer = new StringWriter();
            Console.SetOut(writer);

            Assert.Equal(126, CliApp.Run([
                "exec",
                "session-integration",
                "powershell",
                "-Command",
                "Invoke-WebRequest https://example.com",
                "--output",
                "json"
            ]));

            var payload = writer.ToString();
            Assert.Contains("\"exitCode\":126", payload, StringComparison.Ordinal);
            Assert.Contains("\"policyDecision\":\"deny\"", payload, StringComparison.Ordinal);
            Assert.Contains("No matching network rule.", payload, StringComparison.Ordinal);

            using var store = new SessionStore(Path.Combine(root, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None);
            var session = await store.GetAsync("session-integration", CancellationToken.None);
            Assert.NotNull(session);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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

    [Fact]
    public async Task ShimProcessor_Uses_Hosted_Runspace_For_Inline_PowerShell_Command()
    {
        var root = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-shim-hosted-integration");
        Directory.CreateDirectory(root);
        var originalDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = root;
            await File.WriteAllTextAsync(Path.Combine(root, "default-policy.yml"), """
                command_rules:
                  - name: allow-powershell
                    pattern: "powershell"
                    decision: allow
                """);

            using var store = new SessionStore(Path.Combine(root, ".agentpowershell", "sessions.json"));
            await store.LoadAsync(CancellationToken.None);
            var processor = new ShimCommandProcessor(store, new AgentPowerShellConfig());

            var response = await processor.ExecuteAsync(
                new ShimCommandRequest
                {
                    SessionId = "session-hosted",
                    InvocationName = "powershell",
                    ExecutablePath = RealPowerShellResolver.Resolve("powershell"),
                    WorkingDirectory = root,
                    Arguments = ["-NoProfile", "-Command", "$ExecutionContext.SessionState.LanguageMode"]
                },
                CancellationToken.None);

            Assert.Equal(0, response.ExitCode);
            Assert.Contains("ConstrainedLanguage", response.Stdout, StringComparison.Ordinal);
            Assert.Contains(response.Events, item => item.EventType == "process.executed.powershell-host");
        }
        finally
        {
            Environment.CurrentDirectory = originalDirectory;
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
