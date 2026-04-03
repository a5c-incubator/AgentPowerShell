using System.Collections.ObjectModel;

namespace AgentPowerShell.Core;

public sealed record AgentSession
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;
    public string PolicyPath { get; init; } = "default-policy.yml";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActivityAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; init; } = DateTimeOffset.UtcNow.AddMinutes(30);
    public string Platform { get; init; } = Environment.OSVersion.Platform.ToString();
}

public sealed record SessionSnapshot
{
    public Collection<AgentSession> Sessions { get; init; } = [];
}
