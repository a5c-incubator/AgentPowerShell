using AgentPowerShell.Core;

namespace AgentPowerShell.Daemon;

internal static class PolicyEvaluationHelper
{
    public static (AgentSession Session, PolicyEngine Engine) CreatePolicyEngine(
        SessionStore sessionStore,
        AgentPowerShellConfig config,
        string sessionId,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var session = sessionStore.GetOrCreateAsync(sessionId, workingDirectory, config.Sessions, cancellationToken)
            .GetAwaiter()
            .GetResult();

        if (!File.Exists(session.PolicyPath))
        {
            return (session, new PolicyEngine(new ExecutionPolicy()));
        }

        var policy = new PolicyLoader().LoadFromFile(session.PolicyPath);
        return (session, new PolicyEngine(policy));
    }
}
