using System.Text.Json;

namespace AgentPowerShell.Core;

public sealed class WorkspaceCheckpointManager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _workspaceRoot;
    private readonly string _checkpointRoot;

    public WorkspaceCheckpointManager(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _checkpointRoot = Path.Combine(_workspaceRoot, ".agentpowershell", "checkpoints");
    }

    public async Task<WorkspaceCheckpointRecord> CreateAsync(string? name, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_checkpointRoot);

        var checkpointId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..23];
        var normalizedName = string.IsNullOrWhiteSpace(name) ? checkpointId : name.Trim();
        var checkpointDirectory = Path.Combine(_checkpointRoot, checkpointId);
        var snapshotDirectory = Path.Combine(checkpointDirectory, "snapshot");
        Directory.CreateDirectory(snapshotDirectory);

        var files = new List<string>();
        foreach (var sourcePath in EnumerateWorkspaceFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(_workspaceRoot, sourcePath);
            var destinationPath = Path.Combine(snapshotDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            files.Add(relativePath.Replace('\\', '/'));
        }

        var record = new WorkspaceCheckpointRecord(
            checkpointId,
            normalizedName,
            DateTimeOffset.UtcNow,
            files.OrderBy(path => path, StringComparer.Ordinal).ToArray());

        var metadataPath = Path.Combine(checkpointDirectory, "checkpoint.json");
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(record, JsonOptions), cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<WorkspaceCheckpointRecord>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_checkpointRoot))
        {
            return [];
        }

        var checkpoints = new List<WorkspaceCheckpointRecord>();
        foreach (var directory in Directory.EnumerateDirectories(_checkpointRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = Path.Combine(directory, "checkpoint.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            var record = JsonSerializer.Deserialize<WorkspaceCheckpointRecord>(json, JsonOptions);
            if (record is not null)
            {
                checkpoints.Add(record);
            }
        }

        return checkpoints
            .OrderByDescending(checkpoint => checkpoint.CreatedAt)
            .ToArray();
    }

    public async Task<WorkspaceCheckpointRestorePreview> PreviewRestoreAsync(string checkpointId, CancellationToken cancellationToken)
    {
        var record = await LoadCheckpointAsync(checkpointId, cancellationToken).ConfigureAwait(false);
        var resolvedCheckpointId = record.CheckpointId;
        var snapshotFiles = record.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentFiles = EnumerateWorkspaceFiles()
            .Select(path => Path.GetRelativePath(_workspaceRoot, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toDelete = currentFiles.Where(path => !snapshotFiles.Contains(path)).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var toAdd = snapshotFiles.Where(path => !currentFiles.Contains(path)).OrderBy(path => path, StringComparer.Ordinal).ToArray();

        var toUpdate = new List<string>();
        foreach (var path in snapshotFiles.Intersect(currentFiles, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentPath = Path.Combine(_workspaceRoot, path);
            var snapshotPath = Path.Combine(GetSnapshotRoot(resolvedCheckpointId), path);
            if (!await FilesMatchAsync(currentPath, snapshotPath, cancellationToken).ConfigureAwait(false))
            {
                toUpdate.Add(path);
            }
        }

        return new WorkspaceCheckpointRestorePreview(
            resolvedCheckpointId,
            record.Name,
            toAdd,
            toUpdate.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            toDelete);
    }

    public async Task<WorkspaceCheckpointRestorePreview> RestoreAsync(string checkpointId, CancellationToken cancellationToken)
    {
        var preview = await PreviewRestoreAsync(checkpointId, cancellationToken).ConfigureAwait(false);
        var snapshotRoot = GetSnapshotRoot(preview.CheckpointId);

        foreach (var path in preview.FilesToDelete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(_workspaceRoot, path);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        foreach (var path in preview.FilesToAdd.Concat(preview.FilesToUpdate))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.Combine(snapshotRoot, path);
            var destinationPath = Path.Combine(_workspaceRoot, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        return preview;
    }

    private IEnumerable<string> EnumerateWorkspaceFiles()
    {
        if (!Directory.Exists(_workspaceRoot))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(_workspaceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(_workspaceRoot, file);
            if (IsExcluded(relativePath))
            {
                continue;
            }

            yield return file;
        }
    }

    private bool IsExcluded(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        return normalized.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(".agentpowershell/checkpoints/", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<WorkspaceCheckpointRecord> LoadCheckpointAsync(string checkpointId, CancellationToken cancellationToken)
    {
        if (string.Equals(checkpointId, "latest", StringComparison.OrdinalIgnoreCase))
        {
            var checkpoints = await ListAsync(cancellationToken).ConfigureAwait(false);
            if (checkpoints.Count == 0)
            {
                throw new DirectoryNotFoundException("No checkpoints are available.");
            }

            return checkpoints[0];
        }

        var metadataPath = Path.Combine(_checkpointRoot, checkpointId, "checkpoint.json");
        if (!File.Exists(metadataPath))
        {
            throw new DirectoryNotFoundException($"Checkpoint '{checkpointId}' was not found.");
        }

        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<WorkspaceCheckpointRecord>(json, JsonOptions)
            ?? throw new InvalidDataException($"Checkpoint '{checkpointId}' metadata is invalid.");
    }

    private string GetSnapshotRoot(string checkpointId) => Path.Combine(_checkpointRoot, checkpointId, "snapshot");

    private static async Task<bool> FilesMatchAsync(string currentPath, string snapshotPath, CancellationToken cancellationToken)
    {
        var currentInfo = new FileInfo(currentPath);
        var snapshotInfo = new FileInfo(snapshotPath);
        if (currentInfo.Length != snapshotInfo.Length)
        {
            return false;
        }

        var currentBytes = await File.ReadAllBytesAsync(currentPath, cancellationToken).ConfigureAwait(false);
        var snapshotBytes = await File.ReadAllBytesAsync(snapshotPath, cancellationToken).ConfigureAwait(false);
        return currentBytes.AsSpan().SequenceEqual(snapshotBytes);
    }
}

public sealed record WorkspaceCheckpointRecord(
    string CheckpointId,
    string Name,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Files);

public sealed record WorkspaceCheckpointRestorePreview(
    string CheckpointId,
    string Name,
    IReadOnlyList<string> FilesToAdd,
    IReadOnlyList<string> FilesToUpdate,
    IReadOnlyList<string> FilesToDelete)
{
    public int TotalChanges => FilesToAdd.Count + FilesToUpdate.Count + FilesToDelete.Count;
}
