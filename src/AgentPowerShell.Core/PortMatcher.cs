namespace AgentPowerShell.Core;

internal static class PortMatcher
{
    public static bool IsMatch(int port, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var trimmed = pattern.Trim();
        if (trimmed.Contains('-', StringComparison.Ordinal))
        {
            var parts = trimmed.Split('-', 2, StringSplitOptions.TrimEntries);
            return parts.Length == 2
                && int.TryParse(parts[0], out var start)
                && int.TryParse(parts[1], out var end)
                && port >= start
                && port <= end;
        }

        return int.TryParse(trimmed, out var exact) && port == exact;
    }
}
