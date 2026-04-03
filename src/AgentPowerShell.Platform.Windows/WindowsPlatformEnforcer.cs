using AgentPowerShell.Core;

namespace AgentPowerShell.Platform.Windows;

public sealed class WindowsPlatformEnforcer : IPlatformEnforcer
{
    private readonly string _name = "windows";

    public string Name => _name;
    public string PlatformId => Name;

    public Task ApplyPolicyAsync(ExecutionPolicy policy, CancellationToken cancellationToken)
    {
        _ = WindowsEnforcementPlan.FromPolicy(policy);
        return Task.CompletedTask;
    }
}
