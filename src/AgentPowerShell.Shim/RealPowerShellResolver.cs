namespace AgentPowerShell.Shim;

public static class RealPowerShellResolver
{
    public static string Resolve(string invocationName)
    {
        var configured = Environment.GetEnvironmentVariable("AGENTPOWERSHELL_REAL_SHELL")?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var current = Environment.ProcessPath;
        var candidates = new[]
        {
            "pwsh",
            "powershell"
        };

        foreach (var candidate in candidates)
        {
            var resolved = FindOnPath(candidate);
            if (!string.IsNullOrWhiteSpace(resolved) && !string.Equals(resolved, current, StringComparison.OrdinalIgnoreCase))
            {
                return resolved;
            }
        }

        throw new InvalidOperationException($"Unable to locate a real PowerShell executable for {invocationName}.");
    }

    private static string? FindOnPath(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(segment, executableName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
