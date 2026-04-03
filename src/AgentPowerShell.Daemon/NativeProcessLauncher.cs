using System.Diagnostics;
using AgentPowerShell.Core;
using AgentPowerShell.Platform.Windows;
using AgentPowerShell.Protos;

namespace AgentPowerShell.Daemon;

internal sealed class NativeProcessLauncher
{
    public async Task<CommandExecutionResult> ExecuteAsync(
        ShimCommandRequest request,
        AgentSession session,
        IReadOnlyDictionary<string, string> allowedEnvironmentOverrides,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutablePath(request.ExecutablePath),
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

        foreach (var pair in allowedEnvironmentOverrides)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        startInfo.Environment["AGENTPOWERSHELL_IN_SESSION"] = "1";
        startInfo.Environment["AGENTPOWERSHELL_SESSION_ID"] = session.SessionId;

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
