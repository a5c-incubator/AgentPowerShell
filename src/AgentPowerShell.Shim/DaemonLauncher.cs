using System.Diagnostics;

namespace AgentPowerShell.Shim;

public static class DaemonLauncher
{
    public static Process? TryStart()
    {
        var command = Environment.GetEnvironmentVariable("AGENTPOWERSHELL_DAEMON_CMD")?.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            Arguments = $"/c {command}",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        return Process.Start(startInfo);
    }
}
