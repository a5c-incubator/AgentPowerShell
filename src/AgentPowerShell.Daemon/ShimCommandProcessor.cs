using System.Diagnostics;
using AgentPowerShell.Core;
using AgentPowerShell.Protos;

namespace AgentPowerShell.Daemon;

public sealed class ShimCommandProcessor(SessionStore sessionStore, AgentPowerShellConfig config)
{
    public async Task<ShimCommandResponse> ExecuteAsync(ShimCommandRequest request, CancellationToken cancellationToken)
    {
        var session = await sessionStore.GetOrCreateAsync(
            request.SessionId,
            string.IsNullOrWhiteSpace(request.WorkingDirectory) ? Environment.CurrentDirectory : request.WorkingDirectory,
            config.Sessions,
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

        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory) ? Environment.CurrentDirectory : request.WorkingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = false,
            UseShellExecute = false
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in request.Environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        startInfo.Environment["AGENTPOWERSHELL_IN_SESSION"] = "1";
        startInfo.Environment["AGENTPOWERSHELL_SESSION_ID"] = session.SessionId;

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ShimCommandResponse
        {
            SessionId = session.SessionId,
            ExitCode = process.ExitCode,
            PolicyDecision = decision.Decision.ToString().ToLowerInvariant(),
            Stdout = await stdoutTask.ConfigureAwait(false),
            Stderr = await stderrTask.ConfigureAwait(false),
            Events =
            [
                new ShimEventMessage("process.executed", request.ExecutablePath, DateTimeOffset.UtcNow)
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
