using Microsoft.EntityFrameworkCore;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Todos;
using SharpAgent.Infrastructure.Tests.Support;
using Xunit;

namespace SharpAgent.Infrastructure.Tests.Persistence;

/// <summary>
/// Persistence-level proof of the Phase 1 exit criteria: create/reload from fresh
/// SQLite, and resume producing a NEW run while prior history is retained (AC-05).
/// </summary>
public sealed class SessionReloadAndResumeTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    private static readonly DateTimeOffset Now = new(2026, 8, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Draft_session_survives_a_full_reload_from_sqlite()
    {
        await _database.InitializeAsync();

        string sessionId;

        await using (var writer = _database.OpenContext())
        {
            var session = Domain.Sessions.Session.CreateNew(
                "ws_demo", "Investigate the failing pricing test", SessionMode.Plan, "model_fake", "pol_default", Now);
            sessionId = session.Id;
            await writer.Sessions.AddAsync(session);
            await writer.SaveChangesAsync();
        }

        await using (var reader = _database.OpenContext())
        {
            var reloaded = await reader.Sessions
                .Include(static candidate => candidate.Runs)
                .SingleAsync(candidate => candidate.Id == sessionId);

            Assert.Equal(SessionStatus.Draft, reloaded.Status);
            Assert.Equal("Investigate the failing pricing test", reloaded.Task);
            Assert.Equal(SessionMode.Plan, reloaded.Mode);
            Assert.Null(reloaded.ActiveRunId);
            Assert.Empty(reloaded.Runs);
        }
    }

    [Fact]
    public async Task Resume_creates_a_new_run_and_retains_todos_and_history()
    {
        await _database.InitializeAsync();

        string sessionId;
        string firstRunId;

        // Run 1: start, add todos, then cancel.
        await using (var context = _database.OpenContext())
        {
            var session = Domain.Sessions.Session.CreateNew(
                "ws_demo", "Plan then fix pricing bug", SessionMode.Plan, "model_fake", "pol_default", Now);
            sessionId = session.Id;

            var run = session.BeginRun(Now.AddMinutes(1));
            firstRunId = run.Id;

            await context.TodoItems.AddRangeAsync(
            [
                TodoItem.Create(session.Id, firstRunId, 1, "Read pricing module", Now.AddMinutes(2)),
                TodoItem.Create(session.Id, firstRunId, 2, "Draft the plan", Now.AddMinutes(2)),
            ]);

            session.CancelActiveRun("Cancelled by developer.", Now.AddMinutes(3));

            await context.Sessions.AddAsync(session);
            await context.SaveChangesAsync();
        }

        // Resume: NEW run id, sequence two, prior rows untouched.
        string secondRunId;

        await using (var context = _database.OpenContext())
        {
            var session = await context.Sessions
                .Include(static candidate => candidate.Runs)
                .SingleAsync(candidate => candidate.Id == sessionId);

            Assert.Single(session.Runs);
            Assert.Equal(SessionStatus.Cancelled, session.Status);

            var resumed = session.BeginRun(Now.AddMinutes(4), instruction: "continue", resumeSourceRunId: firstRunId);
            secondRunId = resumed.Id;

            await context.SaveChangesAsync();

            Assert.NotEqual(firstRunId, secondRunId);
            Assert.Equal(2, resumed.Sequence);
        }

        await using (var verifier = _database.OpenContext())
        {
            var session = await verifier.Sessions
                .Include(static candidate => candidate.Runs)
                .SingleAsync(candidate => candidate.Id == sessionId);

            Assert.Equal(SessionStatus.Planning, session.Status);
            Assert.Equal(2, session.Runs.Count);

            var original = session.Runs.Single(candidate => candidate.Id == firstRunId);
            Assert.Equal(RunStatus.Cancelled, original.Status);
            Assert.Equal("Cancelled by developer.", original.StopReason);
            Assert.Equal(1, original.Sequence);

            var todos = await verifier.TodoItems
                .Where(todo => todo.SessionId == sessionId)
                .OrderBy(static todo => todo.Sequence)
                .ToListAsync();

            Assert.Equal(2, todos.Count);
            Assert.All(todos, todo => Assert.Equal(firstRunId, todo.RunId));
        }
    }

    [Fact]
    public async Task Optimistic_concurrency_rejects_stale_session_updates()
    {
        await _database.InitializeAsync();

        await using (var setup = _database.OpenContext())
        {
            var session = Domain.Sessions.Session.CreateNew("ws", "task", SessionMode.Plan, "m", "p", Now);
            await setup.Sessions.AddAsync(session);
            await setup.SaveChangesAsync();
        }

        await using var first = _database.OpenContext();
        await using var second = _database.OpenContext();

        var firstView = await first.Sessions.SingleAsync();
        var secondView = await second.Sessions.SingleAsync();

        firstView.Archive(Now.AddMinutes(1));
        await first.SaveChangesAsync(); // bumps Version to 1

        secondView.Archive(Now.AddMinutes(2)); // still expects Version 0
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    public void Dispose() => _database.Dispose();
}

