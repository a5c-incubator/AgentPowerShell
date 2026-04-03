using System.Diagnostics;
using AgentPowerShell.Core;
using AgentPowerShell.Platform.Windows;
using AgentPowerShell.Protos;

namespace AgentPowerShell.Daemon;

internal sealed class NativeProcessLauncher
{
    private readonly WindowsAppContainerProcessLauncher _appContainerLauncher;

    public NativeProcessLauncher()
        : this(new WindowsAppContainerProcessLauncher())
    {
    }

    internal NativeProcessLauncher(WindowsAppContainerProcessLauncher appContainerLauncher)
    {
        _appContainerLauncher = appContainerLauncher;
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        ShimCommandRequest request,
        AgentSession session,
        IReadOnlyDictionary<string, string> allowedEnvironmentOverrides,
        bool useWindowsAppContainer,
        CancellationToken cancellationToken)
    {
        var executablePath = ResolveExecutablePath(request.ExecutablePath);
        var workingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory) ? Environment.CurrentDirectory : request.WorkingDirectory;
        var environment = BuildEnvironment(allowedEnvironmentOverrides, session.SessionId);

        if (useWindowsAppContainer)
        {
            try
            {
                var isolated = await _appContainerLauncher.ExecuteAsync(
                    executablePath,
                    request.Arguments,
                    workingDirectory,
                    environment,
                    session.SessionId,
                    cancellationToken).ConfigureAwait(false);

                return new CommandExecutionResult(
                    isolated.ExitCode,
                    isolated.Stdout,
                    isolated.Stderr,
                    "process.executed.native.appcontainer",
                    request.ExecutablePath);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new CommandExecutionResult(
                    126,
                    string.Empty,
                    $"Failed to start Windows AppContainer sandbox: {exception.Message}",
                    "process.blocked.native.appcontainer",
                    request.ExecutablePath);
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = false,
            UseShellExecute = false
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var job = WindowsJobObject.TryCreate($"agentpowershell-{session.SessionId}");
        job?.Assign(process);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new CommandExecutionResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false),
            "process.executed.native",
            request.ExecutablePath);
    }

    private static Dictionary<string, string> BuildEnvironment(
        IReadOnlyDictionary<string, string> allowedEnvironmentOverrides,
        string sessionId)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry pair in Environment.GetEnvironmentVariables())
        {
            if (pair.Key is string key && pair.Value is string value)
            {
                environment[key] = value;
            }
        }

        foreach (var pair in allowedEnvironmentOverrides)
        {
            environment[pair.Key] = pair.Value;
        }

        environment["AGENTPOWERSHELL_IN_SESSION"] = "1";
        environment["AGENTPOWERSHELL_SESSION_ID"] = sessionId;
        return environment;
    }

    private static string ResolveExecutablePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return executablePath;
        }

        if (Path.IsPathRooted(executablePath)
            || executablePath.Contains(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || executablePath.Contains(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            return executablePath;
        }

        if (!Path.GetFileNameWithoutExtension(executablePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return executablePath;
        }

        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"),
            OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe") : "/usr/bin/dotnet"
        };

        return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            ?? executablePath;
    }
}
