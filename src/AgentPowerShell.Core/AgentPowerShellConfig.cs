using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using System.Collections.ObjectModel;

namespace AgentPowerShell.Core;

public sealed record AgentPowerShellConfig
{
    public ServerConfig Server { get; init; } = new();
    public AuthConfig Auth { get; init; } = new();
    public LoggingConfig Logging { get; init; } = new();
    public SessionConfig Sessions { get; init; } = new();
    public PolicyConfig Policy { get; init; } = new();
    public EventConfig Events { get; init; } = new();
    public ApprovalConfig Approval { get; init; } = new();
    public LlmProxyConfig LlmProxy { get; init; } = new();
    public ShimConfig Shim { get; init; } = new();
}

public sealed record ServerConfig
{
    public string IpcSocket { get; init; } = "/tmp/agentpowershell.sock";
    public int HttpPort { get; init; } = 9120;
    public string HttpBind { get; init; } = "127.0.0.1";
}

public sealed record AuthConfig
{
    public string Mode { get; init; } = "none";
    public string ApiKey { get; init; } = string.Empty;
    public OidcConfig Oidc { get; init; } = new();
}

public sealed record OidcConfig
{
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = "development-secret";
    public int AccessTokenLifetimeMinutes { get; init; } = 15;
    public int RefreshTokenLifetimeMinutes { get; init; } = 480;
}

public sealed record LoggingConfig
{
    public string Level { get; init; } = "Information";
    public bool Console { get; init; } = true;
    public string File { get; init; } = "logs/agentpowershell.log";
    public bool Structured { get; init; } = true;
}

public sealed record SessionConfig
{
    public int MaxConcurrent { get; init; } = 10;
    public int IdleTimeoutMinutes { get; init; } = 30;
    public int MaxLifetimeMinutes { get; init; } = 480;
    public int ReapIntervalSeconds { get; init; } = 60;
}

public sealed record PolicyConfig
{
    public string DefaultPolicy { get; init; } = "default-policy.yml";
    public bool WatchForChanges { get; init; } = true;
}

public sealed record EventConfig
{
    public Collection<EventStoreConfig> Stores { get; init; } = [];
}

public sealed record EventStoreConfig
{
    public string Type { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public int? MaxSizeMb { get; init; }
    public int? MaxBackups { get; init; }
}

public sealed record ApprovalConfig
{
    public string Mode { get; init; } = "tty";
    public int TimeoutSeconds { get; init; } = 120;
    public string RestApiEndpoint { get; init; } = string.Empty;
    public string TotpSecretsPath { get; init; } = ".agentpowershell/approvals/totp-secrets.json";
    public string WebAuthnSecretsPath { get; init; } = ".agentpowershell/approvals/webauthn-secrets.json";
}

public sealed record LlmProxyConfig
{
    public bool Enabled { get; init; }
    public int ListenPort { get; init; } = 9121;
    public Collection<string> Providers { get; init; } = [];
    public int RequestsPerMinute { get; init; } = 60;
    public int TokensPerMinute { get; init; } = 120000;
}

public sealed record ShimConfig
{
    public string FailMode { get; init; } = "closed";
    public int MaxConsecutiveFailures { get; init; } = 3;
}

public sealed class ConfigLoader
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();
    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public AgentPowerShellConfig LoadFromFile(string path) =>
        _deserializer.Deserialize<AgentPowerShellConfig>(File.ReadAllText(path)) ?? new AgentPowerShellConfig();

    public void SaveToFile(string path, AgentPowerShellConfig config)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, _serializer.Serialize(config));
    }
}
