using AgentPowerShell.Core;

namespace AgentPowerShell.Platform.Linux;

public sealed class LinuxPlatformEnforcer : IPlatformEnforcer
{
    private readonly string _name = "linux";

    public string Name => _name;
    public string PlatformId => Name;

    public Task ApplyPolicyAsync(ExecutionPolicy policy, CancellationToken cancellationToken)
    {
        _ = LinuxEnforcementPlan.FromPolicy(policy);
        return Task.CompletedTask;
    }
}
