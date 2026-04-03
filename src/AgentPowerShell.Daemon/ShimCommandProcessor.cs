using AgentPowerShell.Core;
using AgentPowerShell.Protos;

namespace AgentPowerShell.Daemon;

public sealed class ShimCommandProcessor
{
    private readonly SessionStore _sessionStore;
    private readonly AgentPowerShellConfig _config;
    private readonly HostedPowerShellExecutor _hostedPowerShellExecutor;
    private readonly NativeProcessLauncher _nativeProcessLauncher;

    public ShimCommandProcessor(SessionStore sessionStore, AgentPowerShellConfig config)
        : this(sessionStore, config, new HostedPowerShellExecutor(), new NativeProcessLauncher())
    {
    }

    internal ShimCommandProcessor(
        SessionStore sessionStore,
        AgentPowerShellConfig config,
        HostedPowerShellExecutor hostedPowerShellExecutor,
        NativeProcessLauncher nativeProcessLauncher)
    {
        _sessionStore = sessionStore;
        _config = config;
        _hostedPowerShellExecutor = hostedPowerShellExecutor;
        _nativeProcessLauncher = nativeProcessLauncher;
    }

    public async Task<ShimCommandResponse> ExecuteAsync(ShimCommandRequest request, CancellationToken cancellationToken)
    {
        var session = await _sessionStore.GetOrCreateAsync(
            request.SessionId,
            string.IsNullOrWhiteSpace(request.WorkingDirectory) ? Environment.CurrentDirectory : request.WorkingDirectory,
            _config.Sessions,
            cancellationToken).ConfigureAwait(false);

        var decision = EvaluatePolicy(request, session);
        if (decision.Decision == PolicyDecision.Deny)
        {
            return new ShimCommandResponse
            {
                SessionId = session.SessionId,
                ExitCode = 126,
                PolicyDecision = decision.Decision.ToString().ToLowerInvariant(),
                DenialReason = decision.Message ?? "Denied by policy.",
                Stderr = decision.Message ?? "Denied by policy."
            };
        }

        var networkDecision = EvaluateExplicitNetworkTargets(request, session);
        if (networkDecision is not null)
        {
            return new ShimCommandResponse
            {
                SessionId = session.SessionId,
                ExitCode = 126,
                PolicyDecision = networkDecision.Decision.ToString().ToLowerInvariant(),
                DenialReason = networkDecision.Message ?? "Blocked by network policy.",
                Stderr = networkDecision.Message ?? "Blocked by network policy."
            };
        }

        var environmentDecision = EnvironmentPolicyFilter.Evaluate(request, session);
        if (!environmentDecision.Allowed)
        {
            return new ShimCommandResponse
            {
                SessionId = session.SessionId,
                ExitCode = 126,
                PolicyDecision = "deny",
                DenialReason = environmentDecision.DenialReason ?? "Blocked by environment policy.",
                Stderr = environmentDecision.DenialReason ?? "Blocked by environment policy."
            };
        }

        if (IsUnsupportedInteractiveShellLaunch(request))
        {
            const string message = "Interactive shell sessions are not supported by `exec` yet. Pass an explicit command, for example `powershell.exe -Command Get-Date`.";
            return new ShimCommandResponse
            {
                SessionId = session.SessionId,
                ExitCode = 2,
                PolicyDecision = decision.Decision.ToString().ToLowerInvariant(),
                DenialReason = message,
                Stderr = message
            };
        }

        var execution = _hostedPowerShellExecutor.CanExecute(request)
            ? await _hostedPowerShellExecutor.ExecuteAsync(request, session, cancellationToken).ConfigureAwait(false)
            : await _nativeProcessLauncher.ExecuteAsync(request, session, environmentDecision.AllowedOverrides, cancellationToken).ConfigureAwait(false);

        return new ShimCommandResponse
        {
            SessionId = session.SessionId,
            ExitCode = execution.ExitCode,
            PolicyDecision = decision.Decision.ToString().ToLowerInvariant(),
            Stdout = execution.Stdout,
            Stderr = execution.Stderr,
            Events =
            [
                new ShimEventMessage(execution.EventType, execution.EventDetail, DateTimeOffset.UtcNow)
            ]
        };
    }

    private static EvaluationResult EvaluatePolicy(ShimCommandRequest request, AgentSession session)
    {
        var policyPath = session.PolicyPath;
        if (!File.Exists(policyPath))
        {
            return new EvaluationResult(PolicyDecision.Allow, "no-policy");
        }

        var policy = new PolicyLoader().LoadFromFile(policyPath);
        var commandText = string.Join(' ', new[] { request.InvocationName }.Concat(request.Arguments));
        var engine = new PolicyEngine(policy);
        var result = engine.EvaluateCommand(new CommandRequest(commandText));
        return result.Decision == PolicyDecision.Approve
            ? result with { Decision = PolicyDecision.Allow }
            : result;
    }

    private static EvaluationResult? EvaluateExplicitNetworkTargets(ShimCommandRequest request, AgentSession session)
    {
        var policyPath = session.PolicyPath;
        if (!File.Exists(policyPath))
        {
            return null;
        }

        var policy = new PolicyLoader().LoadFromFile(policyPath);
        var engine = new PolicyEngine(policy);
        foreach (var observation in NetworkIntentInspector.Extract(request))
        {
            var result = engine.EvaluateNetwork(new NetworkRequest(observation.Destination, observation.Port));
            if (result.Decision == PolicyDecision.Deny)
            {
                var message = result.Message ?? $"Network access to {observation.Destination}:{observation.Port} is blocked.";
                return result with { Message = message };
            }
        }

        return null;
    }

    private static bool IsUnsupportedInteractiveShellLaunch(ShimCommandRequest request)
    {
        if (!request.Interactive && request.Arguments.Count != 0)
        {
            return false;
        }

        var executable = Path.GetFileNameWithoutExtension(
            string.IsNullOrWhiteSpace(request.ExecutablePath) ? request.InvocationName : request.ExecutablePath);

        return executable.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || executable.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || executable.Equals("bash", StringComparison.OrdinalIgnoreCase)
            || executable.Equals("sh", StringComparison.OrdinalIgnoreCase)
            || executable.Equals("zsh", StringComparison.OrdinalIgnoreCase)
            || executable.Equals("cmd", StringComparison.OrdinalIgnoreCase);
    }
}
