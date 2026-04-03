using System.Threading.Channels;

namespace AgentPowerShell.Events;

public interface IEventSink
{
    ValueTask PublishAsync(EventRecord record, CancellationToken cancellationToken);
}

public sealed class EventBus : IEventSink
{
    private readonly Channel<EventRecord> _channel = Channel.CreateUnbounded<EventRecord>();
    private readonly List<Func<EventRecord, CancellationToken, ValueTask>> _subscribers = [];

    public IAsyncEnumerable<EventRecord> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Subscribe(Func<EventRecord, CancellationToken, ValueTask> subscriber) =>
        _subscribers.Add(subscriber);

    public async ValueTask PublishAsync(EventRecord record, CancellationToken cancellationToken)
    {
        foreach (var subscriber in _subscribers)
        {
            await subscriber(record, cancellationToken).ConfigureAwait(false);
        }

        await _channel.Writer.WriteAsync(record, cancellationToken).ConfigureAwait(false);
    }
}
