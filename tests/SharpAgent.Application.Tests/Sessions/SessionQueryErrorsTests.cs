using SharpAgent.Application.Tests.Support;
using SharpAgent.Domain.Auditing;
using SharpAgent.Application.Common;
using SharpAgent.Application.Sessions;
using SharpAgent.Domain.Sessions;
using Xunit;

namespace SharpAgent.Application.Tests.Sessions;

public sealed class SessionQueryErrorsTests
{
    [Fact]
    public async Task Unknown_sessions_surface_not_found_for_every_query()
    {
        var fixture = new SessionServiceFixture();

        await Assert.ThrowsAsync<NotFoundException>(
            () => fixture.Service.GetAsync("ses_missing"));
        await Assert.ThrowsAsync<NotFoundException>(
            () => fixture.Service.ReplayEventsAsync("ses_missing"));
        await Assert.ThrowsAsync<NotFoundException>(
            () => fixture.Service.ListTodosAsync("ses_missing"));
        await Assert.ThrowsAsync<NotFoundException>(
            () => fixture.Service.ArchiveAsync("ses_missing", "k"));
    }

    [Fact]
    public async Task Replay_returns_events_in_sequence_order_with_payloads()
    {
        var fixture = new SessionServiceFixture();
        var session = await fixture.Service.CreateAsync(
            new CreateSessionRequest(
                fixture.WorkspaceId, "task", SessionMode.Plan,
                fixture.ModelProfileId, fixture.PolicyProfileId),
            "create-1");

        var events = await fixture.Service.ReplayEventsAsync(session.Id);

        var first = Assert.Single(events);
        Assert.Equal(1, first.Sequence);
        Assert.Equal(AuditEventTypes.SessionCreated, first.Type);
        Assert.Contains("\"workspaceId\"", first.PayloadJson, StringComparison.Ordinal);
    }
}

