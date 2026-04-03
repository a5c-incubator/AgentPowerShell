using Microsoft.Extensions.Hosting;

namespace AgentPowerShell.Daemon;

public sealed class FilesystemMonitorHostedService(
    SessionStore sessionStore,
    FilesystemMonitor filesystemMonitor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var sessions = await sessionStore.ListAsync(stoppingToken).ConfigureAwait(false);
            foreach (var session in sessions)
            {
                await filesystemMonitor.EnsureWatchingAsync(session, stoppingToken).ConfigureAwait(false);
            }

            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);
        }
    }
}
