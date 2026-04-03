using System.Diagnostics;

namespace AgentPowerShell.Core;

public sealed record DaemonLaunchPlan(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

public static class DaemonLaunchResolver
{
    public static DaemonLaunchPlan? Resolve(string workingDirectory, string? processDirectory = null)
    {
        processDirectory ??= AppContext.BaseDirectory;

        var commandOverride = Environment.GetEnvironmentVariable("AGENTPOWERSHELL_DAEMON_CMD")?.Trim();
        if (!string.IsNullOrWhiteSpace(commandOverride))
        {
            return OperatingSystem.IsWindows()
                ? new DaemonLaunchPlan(
                    Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                    ["/c", commandOverride],
                    workingDirectory)
                : new DaemonLaunchPlan("/bin/sh", ["-lc", commandOverride], workingDirectory);
        }

        var explicitPath = Environment.GetEnvironmentVariable("AGENTPOWERSHELL_DAEMON_PATH")?.Trim();
        if (TryResolveBinaryLaunch(explicitPath, workingDirectory, out var explicitPlan))
        {
            return explicitPlan;
        }

        foreach (var candidateDirectory in EnumerateProbeDirectories(workingDirectory, processDirectory))
        {
            if (TryResolveSiblingDaemon(candidateDirectory, workingDirectory, out var siblingPlan))
            {
                return siblingPlan;
            }

            if (TryResolveProjectLaunch(candidateDirectory, workingDirectory, out var projectPlan))
            {
                return projectPlan;
            }
        }

        return null;
    }

    public static ProcessStartInfo CreateStartInfo(DaemonLaunchPlan plan)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.FileName,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static IEnumerable<string> EnumerateProbeDirectories(params string[] roots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var current = Path.GetFullPath(root);
            while (!string.IsNullOrWhiteSpace(current) && seen.Add(current))
            {
                yield return current;

                var parent = Directory.GetParent(current);
                if (parent is null)
                {
                    break;
                }

                current = parent.FullName;
            }
        }
    }

    private static bool TryResolveSiblingDaemon(string directory, string workingDirectory, out DaemonLaunchPlan? plan)
    {
        var executableName = OperatingSystem.IsWindows() ? "AgentPowerShell.Daemon.exe" : "AgentPowerShell.Daemon";
        var executablePath = Path.Combine(directory, executableName);
        if (File.Exists(executablePath))
        {
            plan = new DaemonLaunchPlan(executablePath, [], workingDirectory);
            return true;
        }

        var dllPath = Path.Combine(directory, "AgentPowerShell.Daemon.dll");
        if (TryResolveBinaryLaunch(dllPath, workingDirectory, out plan))
        {
            return true;
        }

        plan = null;
        return false;
    }

    private static bool TryResolveProjectLaunch(string directory, string workingDirectory, out DaemonLaunchPlan? plan)
    {
        var projectPath = Path.Combine(directory, "src", "AgentPowerShell.Daemon", "AgentPowerShell.Daemon.csproj");
        if (!File.Exists(projectPath))
        {
            plan = null;
            return false;
        }

        var dotnetHost = ResolveDotnetHost();
        if (string.IsNullOrWhiteSpace(dotnetHost))
        {
            plan = null;
            return false;
        }

        plan = new DaemonLaunchPlan(dotnetHost, ["run", "--project", projectPath, "--no-build"], directory);
        return true;
    }

    private static bool TryResolveBinaryLaunch(string? path, string workingDirectory, out DaemonLaunchPlan? plan)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            plan = null;
            return false;
        }

        if (string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            var dotnetHost = ResolveDotnetHost();
            if (string.IsNullOrWhiteSpace(dotnetHost))
            {
                plan = null;
                return false;
            }

            plan = new DaemonLaunchPlan(dotnetHost, [path], workingDirectory);
            return true;
        }

        plan = new DaemonLaunchPlan(path, [], workingDirectory);
        return true;
    }

    private static string? ResolveDotnetHost()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"),
            OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe")
                : "/usr/bin/dotnet"
        };

        return candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
    }
}
