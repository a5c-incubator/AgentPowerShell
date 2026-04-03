using System.Text.Json;
using AgentPowerShell.Core;

namespace AgentPowerShell.Daemon;

public sealed class QuarantineService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<QuarantinedFile> QuarantineAsync(AgentSession session, string path, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var isDirectory = Directory.Exists(fullPath);
        if (!isDirectory && !File.Exists(fullPath))
        {
            throw new FileNotFoundException("The requested path does not exist.", fullPath);
        }

        var id = Guid.NewGuid().ToString("N");
        var quarantineRoot = GetSessionRoot(session);
        Directory.CreateDirectory(quarantineRoot);

        var itemName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{id}-{Path.GetFileName(fullPath)}";
        var quarantinePath = Path.Combine(quarantineRoot, itemName);

        if (isDirectory)
        {
            Directory.Move(fullPath, quarantinePath);
        }
        else
        {
            File.Move(fullPath, quarantinePath);
        }

        var record = new QuarantinedFile(id, session.SessionId, fullPath, quarantinePath, isDirectory, DateTimeOffset.UtcNow);
        var metadataPath = GetMetadataPath(session, id);
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(record, JsonOptions), cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<QuarantinedFile> RestoreAsync(string sessionId, string workingDirectory, string quarantineId, CancellationToken cancellationToken)
    {
        var metadataPath = GetMetadataPath(workingDirectory, sessionId, quarantineId);
        if (!File.Exists(metadataPath))
        {
            throw new FileNotFoundException("The quarantine record was not found.", metadataPath);
        }

        var record = JsonSerializer.Deserialize<QuarantinedFile>(
            await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false),
            JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize quarantine metadata.");

        Directory.CreateDirectory(Path.GetDirectoryName(record.OriginalPath)!);

        if (record.IsDirectory)
        {
            if (Directory.Exists(record.OriginalPath))
            {
                throw new IOException($"Cannot restore quarantine item because '{record.OriginalPath}' already exists.");
            }

            Directory.Move(record.QuarantinePath, record.OriginalPath);
        }
        else
        {
            File.Move(record.QuarantinePath, record.OriginalPath, overwrite: true);
        }

        File.Delete(metadataPath);
        return record;
    }

    private static string GetSessionRoot(AgentSession session) =>
        Path.Combine(session.WorkingDirectory, ".agentpowershell", "quarantine", session.SessionId);

    private static string GetMetadataPath(AgentSession session, string quarantineId) =>
        GetMetadataPath(session.WorkingDirectory, session.SessionId, quarantineId);

    private static string GetMetadataPath(string workingDirectory, string sessionId, string quarantineId) =>
        Path.Combine(workingDirectory, ".agentpowershell", "quarantine", sessionId, $"{quarantineId}.json");
}
