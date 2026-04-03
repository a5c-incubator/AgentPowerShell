namespace AgentPowerShell.Events;

public sealed class AppendOnlyEventStore(string path)
{
    public string Path { get; } = path;

    public async Task AppendAsync(EventRecord record, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var serializer = new JsonEventSerializer();
        await File.AppendAllTextAsync(Path, serializer.Serialize(record) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ReadLinesAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        return await File.ReadAllLinesAsync(Path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EventRecord>> ReadEventsAsync(CancellationToken cancellationToken)
    {
        var serializer = new JsonEventSerializer();
        return (await ReadLinesAsync(cancellationToken).ConfigureAwait(false))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(serializer.Deserialize)
            .ToArray();
    }
}
