using AgentPowerShell.Core;
using AgentPowerShell.Protos;

namespace AgentPowerShell.Daemon;

internal sealed record EnvironmentPolicyFilterResult(
    bool Allowed,
    IReadOnlyDictionary<string, string> AllowedOverrides,
    string? DenialReason = null,
    string? VariableName = null);

internal static class EnvironmentPolicyFilter
{
    public static EnvironmentPolicyFilterResult Evaluate(ShimCommandRequest request, AgentSession session)
    {
        var policyPath = session.PolicyPath;
        if (!File.Exists(policyPath))
        {
            return AllowAll(request.Environment);
        }

        var policy = new PolicyLoader().LoadFromFile(policyPath);
        if (policy.EnvRules.Count == 0)
        {
            return AllowAll(request.Environment);
        }

        var engine = new PolicyEngine(policy);
        var currentEnvironment = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .ToDictionary(entry => entry.Key.ToString()!, entry => entry.Value?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var allowedOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in request.Environment)
        {
            if (currentEnvironment.TryGetValue(pair.Key, out var existing)
                && string.Equals(existing, pair.Value, StringComparison.Ordinal))
            {
                continue;
            }

            var evaluation = engine.EvaluateEnvironment(new EnvironmentVariableRequest(pair.Key, "read"));
            var decision = evaluation.Decision == PolicyDecision.Approve ? PolicyDecision.Allow : evaluation.Decision;
            if (decision == PolicyDecision.Deny)
            {
                return new EnvironmentPolicyFilterResult(
                    false,
                    allowedOverrides,
                    evaluation.Message ?? $"Environment variable `{pair.Key}` is blocked by policy.",
                    pair.Key);
            }

            allowedOverrides[pair.Key] = pair.Value;
        }

        return new EnvironmentPolicyFilterResult(true, allowedOverrides);
    }

    private static EnvironmentPolicyFilterResult AllowAll(
        IEnumerable<KeyValuePair<string, string>> environment)
    {
        var values = environment.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        return new EnvironmentPolicyFilterResult(true, values);
    }
}
