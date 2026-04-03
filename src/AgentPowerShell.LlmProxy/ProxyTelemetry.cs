using AgentPowerShell.Events;

namespace AgentPowerShell.LlmProxy;

public sealed class ProxyTelemetry(IEventSink eventSink)
{
    public ValueTask PublishAsync(ProxyResponseContext context, CancellationToken cancellationToken) =>
        eventSink.PublishAsync(
            new LlmEvent(
                DateTimeOffset.UtcNow,
                context.SessionId,
                context.Provider,
                context.Path,
                context.Usage.TotalTokens),
            cancellationToken);
}
