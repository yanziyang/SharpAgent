using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Auditing;
using Xunit;

namespace SharpAgent.Application.Tests.Sessions;

public sealed class SessionEventPublisherTests
{
    [Fact]
    public async Task Subscribers_receive_only_matching_committed_events()
    {
        var publisher = new InMemorySessionEventPublisher();
        await using var subscription = publisher.Subscribe("session_1");

        var eventForOtherSession = AuditEvent.Create("session_2", null, 1, "status", "{}", DateTimeOffset.UtcNow);
        var expected = AuditEvent.Create("session_1", null, 1, "status", "{}", DateTimeOffset.UtcNow);

        publisher.Publish(eventForOtherSession);
        publisher.Publish(expected);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var received = await subscription.ReadAsync(timeout.Token);

        Assert.Same(expected, received);
    }

    [Fact]
    public async Task Disposing_a_subscription_stops_delivery()
    {
        var publisher = new InMemorySessionEventPublisher();
        var subscription = publisher.Subscribe("session_1");
        await subscription.DisposeAsync();

        publisher.Publish(AuditEvent.Create("session_1", null, 1, "status", "{}", DateTimeOffset.UtcNow));

        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var received = await subscription.ReadAsync(timeout.Token);

        Assert.Null(received);
    }
}
