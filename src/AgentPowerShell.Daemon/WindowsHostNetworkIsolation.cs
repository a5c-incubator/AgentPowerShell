using AgentPowerShell.Core;
using AgentPowerShell.Protos;

namespace AgentPowerShell.Daemon;

internal static class WindowsHostNetworkIsolation
{
    private static readonly HashSet<string> NativeNetworkClients = new(StringComparer.OrdinalIgnoreCase)
    {
        "curl",
        "wget",
        "ping",
        "tnc"
    };

    public static bool ShouldUseAppContainer(ExecutionPolicy? policy, ShimCommandRequest request)
    {
        if (!OperatingSystem.IsWindows() || policy is null)
        {
            return false;
        }

        if (policy.NetworkRules.Any(rule => rule.Decision == PolicyDecision.Allow))
        {
            return false;
        }

        var executable = Path.GetFileNameWithoutExtension(
            string.IsNullOrWhiteSpace(request.ExecutablePath) ? request.InvocationName : request.ExecutablePath);

        return NativeNetworkClients.Contains(executable)
            || NetworkIntentInspector.Extract(request).Count != 0;
    }
}
