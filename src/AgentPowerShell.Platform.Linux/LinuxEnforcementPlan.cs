using AgentPowerShell.Core;

namespace AgentPowerShell.Platform.Linux;

public sealed record LinuxEnforcementPlan(
    bool UseSeccomp,
    bool UsePtrace,
    bool UseLandlock,
    bool UseCgroups,
    string SocketPath)
{
    public static LinuxEnforcementPlan FromPolicy(ExecutionPolicy policy) =>
        new(
            UseSeccomp: true,
            UsePtrace: true,
            UseLandlock: true,
            UseCgroups: true,
            SocketPath: "/tmp/agentpowershell.sock");
}
