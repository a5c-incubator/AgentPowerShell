using System.Text.RegularExpressions;
using AgentPowerShell.Core;
using AgentPowerShell.Protos;

namespace AgentPowerShell.Daemon;

internal static partial class NetworkIntentInspector
{
    public static IReadOnlyList<NetworkConnectionObservation> Extract(ShimCommandRequest request)
    {
        var sessionId = request.SessionId;
        var observations = new List<NetworkConnectionObservation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var argument in request.Arguments)
        {
            AddMatchesFromText(argument, sessionId, observations, seen);
        }

        var commandScript = ExtractInlineScript(request.Arguments);
        if (!string.IsNullOrWhiteSpace(commandScript))
        {
            AddMatchesFromText(commandScript, sessionId, observations, seen);
        }

        return observations;
    }

    private static void AddMatchesFromText(
        string text,
        string sessionId,
        List<NetworkConnectionObservation> observations,
        HashSet<string> seen)
    {
        foreach (Match match in AbsoluteUriRegex().Matches(text))
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            {
                continue;
            }

            var port = uri.IsDefaultPort
                ? uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443
                : uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? 80
                : uri.Port
                : uri.Port;

            AddObservation(uri.Host, port, "tcp", sessionId, observations, seen);
        }

        foreach (Match match in HostPortRegex().Matches(text))
        {
            var host = match.Groups["host"].Value;
            if (string.IsNullOrWhiteSpace(host))
            {
                continue;
            }

            var port = int.TryParse(match.Groups["port"].Value, out var parsedPort) ? parsedPort : 443;
            AddObservation(host, port, "tcp", sessionId, observations, seen);
        }

        foreach (Match match in BareHostRegex().Matches(text))
        {
            var host = match.Groups["host"].Value;
            if (string.IsNullOrWhiteSpace(host))
            {
                continue;
            }

            AddObservation(host, 443, "tcp", sessionId, observations, seen);
        }
    }

    private static void AddObservation(
        string host,
        int port,
        string protocol,
        string sessionId,
        List<NetworkConnectionObservation> observations,
        HashSet<string> seen)
    {
        var key = $"{host}:{port}/{protocol}";
        if (!seen.Add(key))
        {
            return;
        }

        observations.Add(new NetworkConnectionObservation(sessionId, host, port, protocol));
    }

    private static string? ExtractInlineScript(System.Collections.ObjectModel.Collection<string> arguments)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            var current = arguments[index];
            if (current.Equals("-Command", StringComparison.OrdinalIgnoreCase)
                || current.Equals("-c", StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    [GeneratedRegex(@"https?://[^\s'""`]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AbsoluteUriRegex();

    [GeneratedRegex(@"(?<!\w)(?<host>[a-z0-9.-]+\.[a-z]{2,})(?::(?<port>\d{1,5}))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostPortRegex();

    [GeneratedRegex(@"(?<![\w/])(curl|wget|ping|tnc|test-netconnection|invoke-webrequest|invoke-restmethod|iwr|irm)\s+(?<host>[a-z0-9.-]+\.[a-z]{2,})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareHostRegex();
}
