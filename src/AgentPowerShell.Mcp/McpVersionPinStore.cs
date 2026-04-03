using System.Text.Json;

namespace AgentPowerShell.Mcp;

public sealed class McpVersionPinStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Dictionary<string, string> _pins = new(StringComparer.OrdinalIgnoreCase);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        _pins.Clear();
        if (!File.Exists(path))
        {
            return;
        }

        var pins = JsonSerializer.Deserialize<Dictionary<string, string>>(
            await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false),
            JsonOptions);

        foreach (var pair in pins ?? new Dictionary<string, string>())
        {
            _pins[pair.Key] = pair.Value;
        }
    }

    public string? GetPinnedHash(string toolName) =>
        _pins.TryGetValue(toolName, out var hash) ? hash : null;

    public async Task PinIfMissingAsync(string toolName, string hash, CancellationToken cancellationToken)
    {
        if (_pins.ContainsKey(toolName))
        {
            return;
        }

        _pins[toolName] = hash;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(_pins, JsonOptions), cancellationToken).ConfigureAwait(false);
    }
}
