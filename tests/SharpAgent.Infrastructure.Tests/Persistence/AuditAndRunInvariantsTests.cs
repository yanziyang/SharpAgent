using Microsoft.EntityFrameworkCore;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Sessions;
using SharpAgent.Infrastructure.Persistence;
using SharpAgent.Infrastructure.Tests.Support;
using Xunit;

namespace SharpAgent.Infrastructure.Tests.Persistence;

public sealed class AuditAndRunInvariantsTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    [Fact]
    public async Task Audit_sequences_are_monotonic_and_replay_is_ordered()
    {
        await _database.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        await using (var context = _database.OpenContext())
        {
            var session = Domain.Sessions.Session.CreateNew("ws", "task", SessionMode.Plan, "m", "p", now);
            var run = session.BeginRun(now.AddMinutes(1));

            foreach (var sequence in new[] { 1L, 2L, 3L })
            {
                await context.AuditEvents.AddAsync(
                    AuditEvent.Create(session.Id, run.Id, sequence, AuditEventTypes.Status, $"{{\"n\":{sequence}}}", now),
                    CancellationToken.None);
            }

            await context.SaveChangesAsync();
        }

        await using (var reloaded = _database.OpenContext())
        {
            var replay = await reloaded.AuditEvents
                .AsNoTracking()
                .Where(auditEvent => auditEvent.SessionId == reloaded.AuditEvents.First().SessionId)
                .OrderBy(auditEvent => auditEvent.Sequence)
                .ToListAsync();

            Assert.Equal([1L, 2L, 3L], replay.Select(static auditEvent => auditEvent.Sequence));
            Assert.All(replay, static auditEvent => Assert.True(auditEvent.OccurredAtUtc <= DateTimeOffset.UtcNow.AddMinutes(1)));
        }
    }

    [Fact]
    public async Task Duplicate_audit_sequence_for_one_session_violates_the_unique_index()
    {
        await _database.InitializeAsync();
        var now = DateTimeOffset.UtcNow;

        await using var context = _database.OpenContext();
        const string sessionId = "ses_dup";
        await context.AuditEvents.AddAsync(AuditEvent.Create(sessionId, null, 1, AuditEventTypes.Status, "{}", now));
        await context.SaveChangesAsync();

        await context.AuditEvents.AddAsync(AuditEvent.Create(sessionId, null, 1, AuditEventTypes.Status, "{}", now));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Only_one_active_run_per_session_can_persist()
    {
        await _database.InitializeAsync();
        var now = DateTimeOffset.UtcNow;

        string sessionId;

        // Persist a session with one active run.
        await using (var setup = _database.OpenContext())
        {
            var session = Domain.Sessions.Session.CreateNew("ws", "task", SessionMode.Execute, "m", "p", now);
            session.BeginRun(now);
            sessionId = session.Id;
            await setup.Sessions.AddAsync(session);
            await setup.SaveChangesAsync();
        }

        // A second ACTIVE run row for the same session must violate the partial index.
        await using (var offender = _database.OpenContext())
        {
            var session = await offender.Sessions.SingleAsync(candidate => candidate.Id == sessionId);

            await AgentRunStartHelper.AddActiveRunRowAsync(offender, session.Id, sequence: 2);

            await Assert.ThrowsAsync<DbUpdateException>(() => offender.SaveChangesAsync());
        }

        // After the first run reaches a terminal state, a second run is allowed.
        await using (var resumeContext = _database.OpenContext())
        {
            var session = resumeContext.Sessions.Include(static candidate => candidate.Runs).First();
            session.FailActiveRun("done", now.AddMinutes(1));
            await resumeContext.SaveChangesAsync();
        }

        await using (var verify = _database.OpenContext())
        {
            var session = verify.Sessions.Include(static candidate => candidate.Runs).First();
            session.BeginRun(now.AddMinutes(5));
            await verify.SaveChangesAsync();

            Assert.Equal(2, session.Runs.Count);
            Assert.Equal(SessionStatus.Executing, session.Status); // Execute-mode resumes into executing
        }
    }

    [Fact]
    public async Task Two_live_leases_for_one_session_are_rejected()
    {
        await _database.InitializeAsync();
        var now = DateTimeOffset.UtcNow;

        await using var context = _database.OpenContext();
        var session = Domain.Sessions.Session.CreateNew("ws", "task", SessionMode.Plan, "m", "p", now);
        await context.Sessions.AddAsync(session);

        await context.RunLeases.AddAsync(RunLease.Acquire(session.Id, "run_a", now));
        await context.RunLeases.AddAsync(RunLease.Acquire(session.Id, "run_b", now));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    public void Dispose() => _database.Dispose();
}

/// <summary>
/// Test helper: creates a legitimate active-run entity through the aggregate, then
/// re-targets it at another session id via EF property entries — proving the DATABASE
/// constraint (not just the aggregate guard) rejects two active runs per session.
/// </summary>
internal static class AgentRunStartHelper
{
    public static async Task<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<AgentRun>> AddActiveRunRowAsync(
        SharpAgentDbContext context,
        string targetSessionId,
        int sequence)
    {
        var donor = Domain.Sessions.Session.CreateNew(
            "ws_donor", "t", SessionMode.Execute, "m", "p", DateTimeOffset.UtcNow);
        var run = donor.BeginRun(DateTimeOffset.UtcNow);

        var entry = await context.AgentRuns.AddAsync(run);

        entry.Property(nameof(AgentRun.SessionId)).CurrentValue = targetSessionId;
        entry.Property(nameof(AgentRun.Sequence)).CurrentValue = sequence;

        return entry;
    }
}
