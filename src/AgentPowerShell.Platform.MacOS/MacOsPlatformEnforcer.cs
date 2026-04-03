using AgentPowerShell.Core;

namespace AgentPowerShell.Platform.MacOS;

public sealed class MacOsPlatformEnforcer : IPlatformEnforcer
{
    private readonly string _name = "macos";

    public string Name => _name;
    public string PlatformId => Name;

    public Task ApplyPolicyAsync(ExecutionPolicy policy, CancellationToken cancellationToken)
    {
        _ = MacOsEnforcementPlan.FromPolicy(policy);
        return Task.CompletedTask;
    }
}
