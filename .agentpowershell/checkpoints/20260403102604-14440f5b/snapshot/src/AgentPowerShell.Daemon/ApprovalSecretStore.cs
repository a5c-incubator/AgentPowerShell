using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace AgentPowerShell.Daemon;

public sealed class ApprovalSecretStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _totpSecretsPath;
    private readonly string _webauthnSecretsPath;

    public ApprovalSecretStore(string totpSecretsPath, string webAuthnSecretsPath)
    {
        _totpSecretsPath = totpSecretsPath;
        _webauthnSecretsPath = webAuthnSecretsPath;
    }

    public async Task<string> GetOrCreateTotpSecretAsync(string sessionId, CancellationToken cancellationToken) =>
        await GetOrCreateSecretAsync(_totpSecretsPath, sessionId, cancellationToken).ConfigureAwait(false);

    public async Task<string> GetOrCreateWebAuthnSecretAsync(string sessionId, CancellationToken cancellationToken) =>
        await GetOrCreateSecretAsync(_webauthnSecretsPath, sessionId, cancellationToken).ConfigureAwait(false);

    private async Task<string> GetOrCreateSecretAsync(string path, string sessionId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var secrets = await ReadSecretsAsync(path, cancellationToken).ConfigureAwait(false);
            if (secrets.TryGetValue(sessionId, out var existing) && !string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }

            var created = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            secrets[sessionId] = created;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(secrets, JsonOptions), cancellationToken).ConfigureAwait(false);
            return created;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<Dictionary<string, string>> ReadSecretsAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
                JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
