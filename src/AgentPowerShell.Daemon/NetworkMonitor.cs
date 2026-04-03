using AgentPowerShell.Core;
using AgentPowerShell.Events;

namespace AgentPowerShell.Daemon;

public sealed class NetworkMonitor(
    SessionStore sessionStore,
    AgentPowerShellConfig config,
    IEventSink eventSink) : INetworkMonitor
{
    public async Task<NetworkInspectionResult> InspectConnectionAsync(NetworkConnectionObservation observation, CancellationToken cancellationToken)
    {
        var (session, engine) = await LoadEngineAsync(observation.SessionId, cancellationToken).ConfigureAwait(false);
        var evaluation = engine.EvaluateNetwork(new NetworkRequest(observation.Destination, observation.Port));

        await eventSink.PublishAsync(
            new AgentPowerShell.Events.NetworkEvent(
                DateTimeOffset.UtcNow,
                session.SessionId,
                observation.Destination,
                observation.Port,
                observation.Protocol),
            cancellationToken).ConfigureAwait(false);

        return new NetworkInspectionResult(evaluation, observation.Destination, observation.Port, observation.Protocol);
    }

    public async Task<DnsInspectionResult> InspectDnsQueryAsync(DnsQueryObservation observation, CancellationToken cancellationToken)
    {
        var (session, engine) = await LoadEngineAsync(observation.SessionId, cancellationToken).ConfigureAwait(false);
        var evaluation = engine.EvaluateDns(observation.Query);
        var answers = observation.Answers?.ToArray() ?? [];

        await eventSink.PublishAsync(
            new DnsEvent(
                DateTimeOffset.UtcNow,
                session.SessionId,
                observation.Query,
                string.Join(',', answers)),
            cancellationToken).ConfigureAwait(false);

        return new DnsInspectionResult(evaluation, observation.Query, answers);
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
}
