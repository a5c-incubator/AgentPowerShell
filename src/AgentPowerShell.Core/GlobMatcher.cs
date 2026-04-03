using System.Text;
using System.Text.RegularExpressions;

namespace AgentPowerShell.Core;

internal static partial class GlobMatcher
{
    public static bool IsMatch(string text, string pattern)
    {
        foreach (var candidate in ExpandAlternation(pattern))
        {
            if (BuildRegex(candidate).IsMatch(text))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> ExpandAlternation(string pattern)
    {
        var start = pattern.IndexOf('{', StringComparison.Ordinal);
        var end = pattern.IndexOf('}', StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            yield return pattern;
            yield break;
        }

        var prefix = pattern[..start];
        var suffix = pattern[(end + 1)..];
        var body = pattern[(start + 1)..end];
        foreach (var option in body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return prefix + option + suffix;
        }
    }

    private static Regex BuildRegex(string pattern)
    {
        var builder = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var current = pattern[i];
            if (current == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                builder.Append(".*");
                i++;
            }
            else if (current == '*')
            {
                builder.Append(@"[^/\\]*");
            }
            else if (current == '?')
            {
                builder.Append('.');
            }
            else
            {
                builder.Append(Regex.Escape(current.ToString()));
            }
        }

        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
