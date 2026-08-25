using System.Collections.Concurrent;
using System.Threading.Channels;
using SharpAgent.Domain.Auditing;

namespace SharpAgent.Application.Abstractions;

/// <summary>
/// Publishes an audit event after its durable transaction has committed. The
/// publisher is deliberately an in-process delivery edge; replay always comes
/// from the append-only audit store.
/// </summary>
public interface ISessionEventPublisher
{
    SessionEventSubscription Subscribe(string sessionId);

    void Publish(AuditEvent auditEvent);
}

/// <summary>One live SSE subscriber. Disposal removes its channel.</summary>
public sealed class SessionEventSubscription : IAsyncDisposable
{
    private readonly Action _dispose;
    private int _disposed;

    internal SessionEventSubscription(ChannelReader<AuditEvent> reader, Action dispose)
    {
        Reader = reader;
        _dispose = dispose;
    }

    internal ChannelReader<AuditEvent> Reader { get; }

    public async ValueTask<AuditEvent?> ReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _dispose();
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Safe in-process fan-out for live session events. Channels are unbounded so a
/// slow browser cannot cause the producer to lose an already committed event;
/// the connection lifetime bounds each subscription.
/// </summary>
public sealed class InMemorySessionEventPublisher : ISessionEventPublisher
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<AuditEvent>>> _subscribers = new(
        StringComparer.Ordinal);

    public SessionEventSubscription Subscribe(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<AuditEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        var sessionSubscribers = _subscribers.GetOrAdd(
            sessionId,
            static _ => new ConcurrentDictionary<Guid, Channel<AuditEvent>>());
        sessionSubscribers[id] = channel;

        return new SessionEventSubscription(
            channel.Reader,
            () =>
            {
                if (sessionSubscribers.TryRemove(id, out var removed))
                {
                    removed.Writer.TryComplete();
                }

                if (sessionSubscribers.IsEmpty)
                {
                    var subscribers = (ICollection<
                        KeyValuePair<string, ConcurrentDictionary<Guid, Channel<AuditEvent>>>>)_subscribers;
                    subscribers.Remove(new KeyValuePair<string, ConcurrentDictionary<Guid, Channel<AuditEvent>>>(
                        sessionId,
                        sessionSubscribers));
                }
            });
    }

    public void Publish(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        if (!_subscribers.TryGetValue(auditEvent.SessionId, out var sessionSubscribers))
        {
            return;
        }

        foreach (var subscriber in sessionSubscribers.Values)
        {
            _ = subscriber.Writer.TryWrite(auditEvent);
        }
    }
}

/// <summary>One queued run execution submitted after the start transaction commits.</summary>
public sealed record RunWorkItem(string SessionId, string RunId);

/// <summary>Application port for durable, in-process run execution.</summary>
public interface IRunCoordinator
{
    ValueTask QueueAsync(RunWorkItem workItem, CancellationToken cancellationToken = default);

    /// <summary>Signals a currently executing run without changing durable state.</summary>
    void RequestCancellation(string sessionId);
}
