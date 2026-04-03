using AgentPowerShell.Core;

namespace AgentPowerShell.LlmProxy;

public sealed class ProviderRouter(AgentPowerShellConfig config)
{
    public ProxyRoute Resolve(string path)
    {
        var provider = DetectProvider(path);
        var upstream = provider switch
        {
            "anthropic" => new Uri("https://api.anthropic.com"),
            _ => new Uri("https://api.openai.com")
        };

        if (config.LlmProxy.Providers.Count > 0)
        {
            var configured = config.LlmProxy.Providers
                .Select(entry => entry.Split('=', 2, StringSplitOptions.TrimEntries))
                .FirstOrDefault(parts => parts.Length == 2 && string.Equals(parts[0], provider, StringComparison.OrdinalIgnoreCase));
            if (configured is { Length: 2 } && Uri.TryCreate(configured[1], UriKind.Absolute, out var uri))
            {
                upstream = uri;
            }
        }

        return new ProxyRoute(provider, upstream);
    }

    public static string DetectProvider(string path)
    {
        if (path.Contains("anthropic", StringComparison.OrdinalIgnoreCase) || path.Contains("/messages", StringComparison.OrdinalIgnoreCase))
        {
            return "anthropic";
        }

        return "openai";
    }
}
