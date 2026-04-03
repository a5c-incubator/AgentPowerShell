using AgentPowerShell.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPowerShell.Daemon;

public sealed class SessionLifecycleService(SessionStore store, ILogger<SessionLifecycleService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pruned = await store.PruneExpiredAsync(stoppingToken).ConfigureAwait(false);
            if (pruned > 0)
            {
                logger.LogInformation("Pruned {PrunedCount} expired sessions.", pruned);
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }
}
