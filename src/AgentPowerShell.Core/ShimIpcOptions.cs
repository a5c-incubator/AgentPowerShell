using System.Runtime.InteropServices;

namespace AgentPowerShell.Core;

public sealed record ShimIpcOptions
{
    public string PipeName { get; init; } = "agentpowershell-shim";
    public string SocketPath { get; init; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? string.Empty
        : Path.Combine(Path.GetTempPath(), "agentpowershell.sock");
    public string Host { get; init; } = "127.0.0.1";
}

public static class ShimIpcEndpoint
{
    public static string ResolvePipeName() =>
        Environment.GetEnvironmentVariable("AGENTPOWERSHELL_PIPE_NAME")?.Trim() is { Length: > 0 } value
            ? value
            : "agentpowershell-shim";

    public static string ResolveSocketPath()
    {
        var configured = Environment.GetEnvironmentVariable("AGENTPOWERSHELL_SOCKET_PATH")?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(Path.GetTempPath(), "agentpowershell.sock");
    }
}
