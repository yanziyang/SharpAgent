using SharpAgent.Application.Tests.Support;
using SharpAgent.Application.Common;
using SharpAgent.Application.Sessions;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Todos;
using Xunit;

namespace SharpAgent.Application.Tests.Sessions;

public sealed class StartResumeCancelTests
{
    private static async Task<(SessionServiceFixture Fixture, SessionDto Session)> NewPlanSessionAsync(
        bool validatedProfile = true)
    {
        var fixture = new SessionServiceFixture(validatedProfile);
        var session = await fixture.Service.CreateAsync(
            new CreateSessionRequest(
                fixture.WorkspaceId,
                "Plan the pricing fix.",
                SessionMode.Plan,
                fixture.ModelProfileId,
                fixture.PolicyProfileId),
            idempotencyKey: $"create-{Guid.NewGuid():N}");

        return (fixture, session);
    }

    [Fact]
    public async Task Starting_a_run_creates_run_one_and_the_session_enters_planning()
    {
        var (fixture, session) = await NewPlanSessionAsync();

        var result = await fixture.Service.StartOrResumeAsync(session.Id, new StartRunRequest(null, null), "run-1");

        Assert.Equal(1, result.Run.Sequence);
        Assert.Equal(RunStatus.Planning, result.Run.Status);
        Assert.Equal(SessionStatus.Planning, result.Session.Status);
        Assert.Equal(result.Run.Id, result.Session.ActiveRunId);
        Assert.NotNull(await fixture.Leases.FindActiveBySessionAsync(session.Id, CancellationToken.None));

        var types = (await fixture.Events.ReplayAsync(session.Id, CancellationToken.None)).Select(static auditEvent => auditEvent.Type).ToList();
        Assert.Equal([AuditEventTypes.SessionCreated, AuditEventTypes.RunStarted], types);
    }

    [Fact]
    public async Task A_second_start_while_active_is_rejected_with_session_active()
    {
        var (fixture, session) = await NewPlanSessionAsync();
        await fixture.Service.StartOrResumeAsync(session.Id, new StartRunRequest(null, null), "run-1");

        var conflict = await Assert.ThrowsAsync<ConflictException>(
            () => fixture.Service.StartOrResumeAsync(session.Id, new StartRunRequest(null, null), "run-2"));

        Assert.Equal("session_active", conflict.Code);
    }

    [Fact]
    public async Task Cancel_stops_the_active_run_releases_the_lease_and_audits()
    {
        var (fixture, session) = await NewPlanSessionAsync();
        var started = await fixture.Service.StartOrResumeAsync(session.Id, new StartRunRequest(null, null), "run-1");

        var cancelled = await fixture.Service.CancelAsync(session.Id, "cancel-1");

        Assert.Equal(SessionStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.ActiveRunId);

        var run = cancelled.Runs.Single(candidate => candidate.Id == started.Run.Id);
        Assert.Equal(RunStatus.Cancelled, run.Status);
        Assert.NotNull(run.EndedAtUtc);
        Assert.Null(await fixture.Leases.FindActiveBySessionAsync(session.Id, CancellationToken.None));

        var replayedEvents = await fixture.Events.ReplayAsync(session.Id, CancellationToken.None);
        var last = replayedEvents[^1];
        Assert.Equal(AuditEventTypes.RunCancelled, last.Type);
    }

    [Fact]
    public async Task Cancelling_without_an_active_run_conflicts()
    {
        var (fixture, session) = await NewPlanSessionAsync();

        var conflict = await Assert.ThrowsAsync<ConflictException>(
            () => fixture.Service.CancelAsync(session.Id, "cancel-none"));

        Assert.Equal("no_active_run", conflict.Code);
    }

    [Fact]
    public async Task Resume_after_cancellation_creates_a_new_run_and_retains_history()
    {
        var (fixture, session) = await NewPlanSessionAsync();
        var first = await fixture.Service.StartOrResumeAsync(session.Id, new StartRunRequest("first pass", null), "run-1");

        // Todos created during the first run must survive the resume (AC-05).
        await fixture.Todos.AddRangeAsync(
            [
                TodoItem.Create(session.Id, first.Run.Id, 1, "Read pricing module", fixture.Clock.UtcNow),
                TodoItem.Create(session.Id, first.Run.Id, 2, "Draft plan", fixture.Clock.UtcNow),
            ],
            CancellationToken.None);

        await fixture.Service.CancelAsync(session.Id, "cancel-1");
        var resumed = await fixture.Service.StartOrResumeAsync(
            session.Id,
            new StartRunRequest("continue with tests", first.Run.Id),
            "run-2");

        Assert.NotEqual(first.Run.Id, resumed.Run.Id);          // different run identifier
        Assert.Equal(2, resumed.Run.Sequence);
        Assert.Equal(first.Run.Id, resumed.Run.ResumeSourceRunId);

        var storedSession = fixture.Sessions.Snapshot.Single(candidate => candidate.Id == session.Id);
        Assert.Equal("continue with tests", storedSession.LastInstruction);

        // Prior history intact: two runs, original run unchanged, todos still listed.
        Assert.Equal(2, resumed.Session.Runs.Count);
        var original = resumed.Session.Runs.Single(candidate => candidate.Id == first.Run.Id);
        Assert.Equal(RunStatus.Cancelled, original.Status);
        Assert.Equal(1, original.Sequence);

        var todos = await fixture.Service.ListTodosAsync(session.Id);
        Assert.Equal(2, todos.Count);
        Assert.All(todos, todo => Assert.Equal(first.Run.Id, todo.RunId));

        var sequences = (await fixture.Events.ReplayAsync(session.Id, CancellationToken.None)).Select(static auditEvent => auditEvent.Sequence);
        Assert.Equal([1L, 2L, 3L, 4L], sequences);
    }

    [Fact]
    public async Task Resume_rejects_a_foreign_resume_source()
    {
        var (fixture, session) = await NewPlanSessionAsync();
        await fixture.Service.StartOrResumeAsync(session.Id, new StartRunRequest(null, null), "run-1");
        await fixture.Service.CancelAsync(session.Id, "cancel-1");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.StartOrResumeAsync(
                session.Id,
                new StartRunRequest(null, ResumeFromRunId: "run_from_other_session"),
                "run-2"));
    }

    [Fact]
    public async Task Archived_sessions_cannot_start_runs_until_restored()
    {
        var (fixture, session) = await NewPlanSessionAsync();
        var started = await fixture.Service.StartOrResumeAsync(session.Id, new StartRunRequest(null, null), "run-1");
        await fixture.Service.CancelAsync(session.Id, "cancel-1");
        await fixture.Service.ArchiveAsync(session.Id, "archive-1");

        var archived = await fixture.Service.GetAsync(session.Id);
        Assert.True(archived.Archived);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.StartOrResumeAsync(session.Id, new StartRunRequest(null, started.Run.Id), "run-2"));
        Assert.Equal("session_archived", conflict.Code);

        await fixture.Service.RestoreAsync(session.Id, "restore-1");
        var restored = await fixture.Service.GetAsync(session.Id);
        Assert.False(restored.Archived);

        var resumed = await fixture.Service.StartOrResumeAsync(
            session.Id, new StartRunRequest(null, started.Run.Id), "run-2");
        Assert.Equal(2, resumed.Run.Sequence);
    }

    [Fact]
    public async Task Profile_losing_execute_eligibility_blocks_new_execute_runs()
    {
        var executeFixture = new SessionServiceFixture(validatedStreamingToolProfile: true);
        var executeSession = await executeFixture.Service.CreateAsync(
            new CreateSessionRequest(
                executeFixture.WorkspaceId,
                "Apply the fix.",
                SessionMode.Execute,
                executeFixture.ModelProfileId,
                executeFixture.PolicyProfileId),
            idempotencyKey: "create-exec");

        var profile = executeFixture.Profiles.Snapshot.Single();
        profile.MarkValidationFailed("Provider smoke failed.", executeFixture.Clock.UtcNow);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            executeFixture.Service.StartOrResumeAsync(executeSession.Id, new StartRunRequest(null, null), "run-1"));

        Assert.Equal("profile_not_executable", conflict.Code);
    }

    [Fact]
    public async Task Listing_hides_archived_by_default_and_orders_by_recency()
    {
        var (fixture, older) = await NewPlanSessionAsync();
        await fixture.Service.CreateAsync(
            new CreateSessionRequest(
                fixture.WorkspaceId, "Second task", SessionMode.Plan,
                fixture.ModelProfileId, fixture.PolicyProfileId),
            "create-2");
        await fixture.Service.ArchiveAsync(older.Id, "archive-older");

        var visible = await fixture.Service.ListAsync(1, 20, includeArchived: false);
        var everything = await fixture.Service.ListAsync(1, 20, includeArchived: true);

        Assert.Single(visible);
        Assert.DoesNotContain(visible, summary => summary.Id == older.Id);
        Assert.Equal(2, everything.Count);
        Assert.True(everything[0].CreatedAtUtc >= everything[1].CreatedAtUtc);
    }
}




