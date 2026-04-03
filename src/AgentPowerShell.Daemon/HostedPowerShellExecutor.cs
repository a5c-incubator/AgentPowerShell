using System.Management.Automation;
using System.Management.Automation.Runspaces;
using AgentPowerShell.Core;
using AgentPowerShell.Protos;

namespace AgentPowerShell.Daemon;

internal sealed class HostedPowerShellExecutor
{
    private static readonly HashSet<string> BlockedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "Add-Type",
        "Invoke-Command",
        "Invoke-Expression",
        "Start-Job",
        "Start-Process",
        "Enter-PSSession",
        "Exit-PSSession",
        "New-PSSession",
        "Remove-PSSession"
    };

    private static readonly HashSet<string> BlockedAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "iex"
    };

    public bool CanExecute(ShimCommandRequest request) =>
        IsPowerShellExecutable(request.ExecutablePath, request.InvocationName)
        && TryGetInlineCommand(request.Arguments, out _);

    public async Task<CommandExecutionResult> ExecuteAsync(
        ShimCommandRequest request,
        AgentSession session,
        CancellationToken cancellationToken)
    {
        _ = TryGetInlineCommand(request.Arguments, out var commandText);
        commandText ??= string.Empty;

        return await Task.Run(() =>
        {
            using var runspace = RunspaceFactory.CreateRunspace(CreateInitialSessionState());
            runspace.Open();
            runspace.SessionStateProxy.Path.SetLocation(session.WorkingDirectory);
            runspace.SessionStateProxy.SetVariable("AgentPowerShellSessionId", session.SessionId);
            runspace.SessionStateProxy.SetVariable("AgentPowerShellWorkingDirectory", session.WorkingDirectory);

            using var powerShell = PowerShell.Create();
            powerShell.Runspace = runspace;
            powerShell.AddScript(commandText, useLocalScope: true);

            try
            {
                var results = powerShell.Invoke();
                var stdout = string.Join(Environment.NewLine, results.Select(result => result?.ToString() ?? string.Empty));
                var stderr = string.Join(Environment.NewLine, powerShell.Streams.Error.Select(error => error.ToString()));
                var exitCode = powerShell.HadErrors ? 1 : 0;
                return new CommandExecutionResult(
                    exitCode,
                    stdout,
                    stderr,
                    "process.executed.powershell-host",
                    request.ExecutablePath);
            }
            catch (RuntimeException exception)
            {
                return new CommandExecutionResult(
                    1,
                    string.Empty,
                    exception.Message,
                    "process.executed.powershell-host",
                    request.ExecutablePath);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static InitialSessionState CreateInitialSessionState()
    {
        var state = InitialSessionState.CreateDefault2();
        state.LanguageMode = PSLanguageMode.ConstrainedLanguage;

        foreach (var command in state.Commands
                     .Where(entry => BlockedCommands.Contains(entry.Name))
                     .ToArray())
        {
            state.Commands.Remove(command.Name, command.CommandType);
        }

        foreach (var alias in state.Commands
                     .OfType<SessionStateAliasEntry>()
                     .Where(entry => BlockedAliases.Contains(entry.Name))
                     .ToArray())
        {
            state.Commands.Remove(alias.Name, alias.CommandType);
        }

        return state;
    }

    private static bool IsPowerShellExecutable(string executablePath, string invocationName)
    {
        var value = string.IsNullOrWhiteSpace(executablePath) ? invocationName : executablePath;
        var executable = Path.GetFileNameWithoutExtension(value);
        return executable.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || executable.Equals("powershell", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetInlineCommand(
        System.Collections.ObjectModel.Collection<string> arguments,
        out string? commandText)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index].Equals("-Command", StringComparison.OrdinalIgnoreCase)
                || arguments[index].Equals("-c", StringComparison.OrdinalIgnoreCase))
            {
                commandText = arguments[index + 1];
                return !string.IsNullOrWhiteSpace(commandText);
            }
        }

        commandText = null;
        return false;
    }
}
