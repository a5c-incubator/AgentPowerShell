using System.Diagnostics;
using AgentPowerShell.Core;

namespace AgentPowerShell.Shim;

public static class DaemonLauncher
{
    public static Process? TryStart(string workingDirectory)
    {
        var plan = DaemonLaunchResolver.Resolve(workingDirectory, AppContext.BaseDirectory);
        if (plan is null)
        {
            return null;
        }

        return Process.Start(DaemonLaunchResolver.CreateStartInfo(plan));
    }
}
