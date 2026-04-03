using AgentPowerShell.Core;

namespace AgentPowerShell.Platform.Windows;

public sealed record WindowsEnforcementPlan(
    bool UseJobObjects,
    bool UseAppContainer,
    bool UseEtw,
    bool UseConPty,
    string NamedPipeName)
{
    public static WindowsEnforcementPlan FromPolicy(ExecutionPolicy policy) =>
        new(
            UseJobObjects: true,
            UseAppContainer: true,
            UseEtw: true,
            UseConPty: true,
            NamedPipeName: "agentpowershell-shim");
}
