namespace AgentPowerShell.Core;

public sealed class PolicyEngine(ExecutionPolicy policy)
{
    public ExecutionPolicy Policy { get; } = policy;

    public EvaluationResult EvaluateFile(FileAccessRequest request)
    {
        foreach (var rule in Policy.FileRules)
        {
            if (!GlobMatcher.IsMatch(request.Path, rule.Pattern))
            {
                continue;
            }

            if (rule.Operations.Count != 0 && !rule.Operations.Any(operation => string.Equals(operation, request.Operation, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return new EvaluationResult(rule.Decision, rule.Name, rule.Message);
        }

        return EvaluationResult.DefaultDeny("file");
    }

    public EvaluationResult EvaluateCommand(CommandRequest request)
    {
        foreach (var rule in Policy.CommandRules)
        {
            if (GlobMatcher.IsMatch(request.ExecutableName, rule.Pattern)
                || GlobMatcher.IsMatch(request.ExecutableStem, rule.Pattern)
                || GlobMatcher.IsMatch(request.CommandLine, rule.Pattern))
            {
                return new EvaluationResult(rule.Decision, rule.Name, rule.Message);
            }
        }

        return EvaluationResult.DefaultDeny("command");
    }

    public EvaluationResult EvaluateNetwork(NetworkRequest request)
    {
        foreach (var rule in Policy.NetworkRules)
        {
            if (!GlobMatcher.IsMatch(request.Domain, rule.Domain))
            {
                continue;
            }

            if (rule.Ports.Count != 0 && !rule.Ports.Any(port => PortMatcher.IsMatch(request.Port, port)))
            {
                continue;
            }

            return new EvaluationResult(rule.Decision, rule.Name, rule.Message);
        }

        return EvaluationResult.DefaultDeny("network");
    }

    public EvaluationResult EvaluateDns(string domain) =>
        EvaluateNetwork(new NetworkRequest(domain, 53));

    public EvaluationResult EvaluateEnvironment(EnvironmentVariableRequest request)
    {
        foreach (var rule in Policy.EnvRules)
        {
            if (!GlobMatcher.IsMatch(request.Variable, rule.Pattern))
            {
                continue;
            }

            if (rule.Actions.Count != 0 && !rule.Actions.Any(action => string.Equals(action, request.Action, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            return new EvaluationResult(rule.Decision, rule.Name, rule.Message);
        }

        return EvaluationResult.DefaultDeny("environment");
    }
}
