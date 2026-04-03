using System.Collections.ObjectModel;
using System.Text.Json;
using AgentPowerShell.Core;

namespace AgentPowerShell.Daemon;

public sealed class SessionStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly TimeSpan PersistRetryDelay = TimeSpan.FromMilliseconds(50);
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, AgentSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public SessionStore(string path)
    {
        _path = path;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _sessions.Clear();
            if (!File.Exists(_path))
            {
                return;
            }

            var snapshot = JsonSerializer.Deserialize<SessionSnapshot>(
                await ReadSharedTextAsync(_path, cancellationToken).ConfigureAwait(false),
                JsonOptions);

            foreach (var session in snapshot?.Sessions ?? [])
            {
                _sessions[session.SessionId] = session;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentSession> GetOrCreateAsync(string? requestedSessionId, string workingDirectory, SessionConfig config, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            PruneExpiredSessionsUnsafe(now);

            if (!string.IsNullOrWhiteSpace(requestedSessionId) && _sessions.TryGetValue(requestedSessionId, out var existing))
            {
                var refreshed = existing with
                {
                    WorkingDirectory = workingDirectory,
                    LastActivityAt = now,
                    ExpiresAt = now.AddMinutes(config.IdleTimeoutMinutes)
                };

                _sessions[requestedSessionId] = refreshed;
                await PersistUnsafeAsync(cancellationToken).ConfigureAwait(false);
                return refreshed;
            }

            if (_sessions.Count >= config.MaxConcurrent)
            {
                var oldest = _sessions.Values.OrderBy(session => session.LastActivityAt).First();
                _sessions.Remove(oldest.SessionId);
            }

            var session = new AgentSession
            {
                SessionId = string.IsNullOrWhiteSpace(requestedSessionId) ? Guid.NewGuid().ToString("N") : requestedSessionId,
                WorkingDirectory = workingDirectory,
                PolicyPath = Path.Combine(workingDirectory, "default-policy.yml"),
                CreatedAt = now,
                LastActivityAt = now,
                ExpiresAt = now.AddMinutes(config.IdleTimeoutMinutes)
            };

            _sessions[session.SessionId] = session;
            await PersistUnsafeAsync(cancellationToken).ConfigureAwait(false);
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> PruneExpiredAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var before = _sessions.Count;
            PruneExpiredSessionsUnsafe(DateTimeOffset.UtcNow);
            if (_sessions.Count != before)
            {
                await PersistUnsafeAsync(cancellationToken).ConfigureAwait(false);
            }

            return before - _sessions.Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AgentSession?> GetAsync(string sessionId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _sessions.TryGetValue(sessionId, out var session) ? session : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string sessionId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _sessions.Remove(sessionId);
            await PersistUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AgentSession>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _sessions.Values.OrderBy(session => session.CreatedAt).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void PruneExpiredSessionsUnsafe(DateTimeOffset now)
    {
        foreach (var expired in _sessions.Values.Where(session => session.ExpiresAt <= now).Select(session => session.SessionId).ToArray())
        {
            _sessions.Remove(expired);
        }
    }

    private async Task PersistUnsafeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var snapshot = new SessionSnapshot { Sessions = new Collection<AgentSession>(_sessions.Values.OrderBy(session => session.CreatedAt).ToList()) };
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await WriteAtomicallyAsync(_path, json, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadSharedTextAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteAtomicallyAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        var tempPath = System.IO.Path.Combine(directory, $"{System.IO.Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, contents, cancellationToken).ConfigureAwait(false);

            for (var attempt = 0; attempt < 5; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(path))
                    {
                        File.Copy(tempPath, path, overwrite: true);
                    }
                    else
                    {
                        File.Move(tempPath, path);
                        tempPath = string.Empty;
                    }

                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    await Task.Delay(PersistRetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public void Dispose() => _gate.Dispose();
}
