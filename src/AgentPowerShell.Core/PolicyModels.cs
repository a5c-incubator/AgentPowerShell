using System.Collections.ObjectModel;

namespace AgentPowerShell.Core;

public sealed record ExecutionPolicy
{
    public string Version { get; init; } = "1";
    public string Name { get; init; } = "default";
    public string Description { get; init; } = string.Empty;
    public Collection<FileRule> FileRules { get; init; } = [];
    public Collection<CommandRule> CommandRules { get; init; } = [];
    public Collection<NetworkRule> NetworkRules { get; init; } = [];
    public Collection<EnvRule> EnvRules { get; init; } = [];
}

public sealed record FileRule
{
    public string Name { get; init; } = string.Empty;
    public string Pattern { get; init; } = "*";
    public Collection<string> Operations { get; init; } = [];
    public PolicyDecision Decision { get; init; } = PolicyDecision.Deny;
    public string? Message { get; init; }
}

public sealed record CommandRule
{
    public string Name { get; init; } = string.Empty;
    public string Pattern { get; init; } = "*";
    public PolicyDecision Decision { get; init; } = PolicyDecision.Deny;
    public string? Message { get; init; }
}

public sealed record NetworkRule
{
    public string Name { get; init; } = string.Empty;
    public string Domain { get; init; } = "*";
    public Collection<string> Ports { get; init; } = [];
    public PolicyDecision Decision { get; init; } = PolicyDecision.Deny;
    public string? Message { get; init; }
}

public sealed record EnvRule
{
    public string Name { get; init; } = string.Empty;
    public string Pattern { get; init; } = "*";
    public Collection<string> Actions { get; init; } = [];
    public PolicyDecision Decision { get; init; } = PolicyDecision.Deny;
    public string? Message { get; init; }
}

public sealed record EvaluationResult(PolicyDecision Decision, string RuleName, string? Message = null)
{
    public static EvaluationResult DefaultDeny(string category) =>
        new(PolicyDecision.Deny, $"default-{category}-deny", $"No matching {category} rule.");
}
