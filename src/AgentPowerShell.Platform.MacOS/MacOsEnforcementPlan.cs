using AgentPowerShell.Core;

namespace AgentPowerShell.Platform.MacOS;

public sealed record MacOsEnforcementPlan(
    bool UseEndpointSecurity,
    bool UseSandboxExec,
    bool UseNetworkExtension,
    bool UseUnixSockets,
    string LaunchAgentLabel)
{
    public static MacOsEnforcementPlan FromPolicy(ExecutionPolicy policy) =>
        new(
            UseEndpointSecurity: true,
            UseSandboxExec: true,
            UseNetworkExtension: true,
            UseUnixSockets: true,
            LaunchAgentLabel: "com.agentpowershell.daemon");
}
