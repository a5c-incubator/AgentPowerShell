using System.Collections.Concurrent;
using AgentPowerShell.Core;
using AgentPowerShell.Events;

namespace AgentPowerShell.Daemon;

public sealed class FilesystemMonitor(
    SessionStore sessionStore,
    AgentPowerShellConfig config,
    QuarantineService quarantineService,
    IEventSink eventSink) : IFilesystemMonitor, IDisposable
{
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);

    public Task EnsureWatchingAsync(AgentSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_watchers.ContainsKey(session.SessionId) || !Directory.Exists(session.WorkingDirectory))
        {
            return Task.CompletedTask;
        }

        var watcher = new FileSystemWatcher(session.WorkingDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.Attributes
                | NotifyFilters.CreationTime
                | NotifyFilters.DirectoryName
                | NotifyFilters.FileName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        watcher.Created += (_, args) => _ = PublishWatcherEventAsync(session.SessionId, args.FullPath, "created");
        watcher.Changed += (_, args) => _ = PublishWatcherEventAsync(session.SessionId, args.FullPath, "changed");
        watcher.Deleted += (_, args) => _ = PublishWatcherEventAsync(session.SessionId, args.FullPath, "deleted");
        watcher.Renamed += (_, args) =>
        {
            _ = PublishWatcherEventAsync(session.SessionId, args.OldFullPath, "renamed-from");
            _ = PublishWatcherEventAsync(session.SessionId, args.FullPath, "renamed-to");
        };

        if (!_watchers.TryAdd(session.SessionId, watcher))
        {
            watcher.Dispose();
        }

        return Task.CompletedTask;
    }

    public async Task<FileInspectionResult> RecordAccessAsync(FileAccessObservation observation, CancellationToken cancellationToken)
    {
        var (session, engine) = await LoadEngineAsync(observation.SessionId, cancellationToken).ConfigureAwait(false);
        await EnsureWatchingAsync(session, cancellationToken).ConfigureAwait(false);

        var evaluation = engine.EvaluateFile(new FileAccessRequest(observation.Path, observation.Operation));
        await PublishEventAsync(session.SessionId, observation.Path, observation.Operation, evaluation.Decision, cancellationToken).ConfigureAwait(false);
        return new FileInspectionResult(evaluation, observation.Path, observation.Operation);
    }

    public async Task<FileInspectionResult> RequestDeleteAsync(FileDeleteRequest request, CancellationToken cancellationToken)
    {
        var (session, engine) = await LoadEngineAsync(request.SessionId, cancellationToken).ConfigureAwait(false);
        await EnsureWatchingAsync(session, cancellationToken).ConfigureAwait(false);

        var evaluation = engine.EvaluateFile(new FileAccessRequest(request.Path, "delete"));
        if (evaluation.Decision == PolicyDecision.Deny)
        {
            await PublishEventAsync(session.SessionId, request.Path, "delete", evaluation.Decision, cancellationToken).ConfigureAwait(false);
            return new FileInspectionResult(evaluation, request.Path, "delete");
        }

        var quarantine = await quarantineService.QuarantineAsync(session, request.Path, cancellationToken).ConfigureAwait(false);
        await PublishEventAsync(session.SessionId, request.Path, "delete", evaluation.Decision, cancellationToken).ConfigureAwait(false);
        return new FileInspectionResult(evaluation, request.Path, "delete", quarantine);
    }

    public async Task<QuarantinedFile> RestoreAsync(string sessionId, string quarantineId, CancellationToken cancellationToken)
    {
        var session = await sessionStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? await sessionStore.GetOrCreateAsync(sessionId, Environment.CurrentDirectory, config.Sessions, cancellationToken).ConfigureAwait(false);
        var restored = await quarantineService.RestoreAsync(session.SessionId, session.WorkingDirectory, quarantineId, cancellationToken).ConfigureAwait(false);
        await PublishEventAsync(session.SessionId, restored.OriginalPath, "restore", PolicyDecision.Allow, cancellationToken).ConfigureAwait(false);
        return restored;
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private async Task<(AgentSession Session, PolicyEngine Engine)> LoadEngineAsync(string sessionId, CancellationToken cancellationToken)
    {
        var session = await sessionStore.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? await sessionStore.GetOrCreateAsync(sessionId, Environment.CurrentDirectory, config.Sessions, cancellationToken).ConfigureAwait(false);
        var policy = File.Exists(session.PolicyPath)
            ? new PolicyLoader().LoadFromFile(session.PolicyPath)
            : new ExecutionPolicy();
        return (session, new PolicyEngine(policy));
    }

    private Task PublishWatcherEventAsync(string sessionId, string path, string operation) =>
        PublishEventAsync(sessionId, path, operation, PolicyDecision.Allow, CancellationToken.None).AsTask();

    private ValueTask PublishEventAsync(
        string sessionId,
        string path,
        string operation,
        PolicyDecision decision,
        CancellationToken cancellationToken) =>
        eventSink.PublishAsync(
            new AgentPowerShell.Events.FileEvent(
                DateTimeOffset.UtcNow,
                sessionId,
                path,
                operation,
                decision.ToString().ToLowerInvariant()),
            cancellationToken);
}
