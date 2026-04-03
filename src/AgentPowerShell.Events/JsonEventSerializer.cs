using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPowerShell.Events;

public sealed class JsonEventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Serialize(EventRecord record) => JsonSerializer.Serialize(record, record.GetType(), Options);

    public EventRecord Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        var eventType = document.RootElement.GetProperty("eventType").GetString();

        return eventType switch
        {
            "file" => JsonSerializer.Deserialize<FileEvent>(json, Options)!,
            "process" => JsonSerializer.Deserialize<ProcessEvent>(json, Options)!,
            "network" => JsonSerializer.Deserialize<NetworkEvent>(json, Options)!,
            "dns" => JsonSerializer.Deserialize<DnsEvent>(json, Options)!,
            "approval" => JsonSerializer.Deserialize<ApprovalEvent>(json, Options)!,
            "pty" => JsonSerializer.Deserialize<PtyEvent>(json, Options)!,
            "llm" => JsonSerializer.Deserialize<LlmEvent>(json, Options)!,
            _ => throw new InvalidOperationException($"Unknown event type '{eventType}'.")
        };
    }
}
