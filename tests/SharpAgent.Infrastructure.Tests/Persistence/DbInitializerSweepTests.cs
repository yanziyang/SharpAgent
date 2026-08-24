using SharpAgent.TestKit.Fakes;
using SharpAgent.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharpAgent.Domain.Sessions;
using SharpAgent.Infrastructure.Persistence;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Infrastructure.Tests.Persistence;

/// <summary>Startup recovery: abandoned active runs become interrupted and resumable.</summary>
public sealed class DbInitializerSweepTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    private static readonly DateTimeOffset Now = new(2026, 8, 23, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Abandoned_active_runs_are_interrupted_with_released_leases_and_events()
    {
        await _database.InitializeAsync();

        string sessionId;
        string runId;

        // Simulate a crash mid-run WITHOUT running the initializer yet.
        await using (var setup = _database.OpenContext())
        {
            var session = Domain.Sessions.Session.CreateNew("ws", "t", SessionMode.Execute, "m", "p", Now);
            sessionId = session.Id;
            var run = session.BeginRun(Now);
            runId = run.Id;

            await setup.Sessions.AddAsync(session);

            var lease = RunLease.Acquire(sessionId, runId, Now);
            await setup.RunLeases.AddAsync(lease);

            // Seed an expired idempotency record to prove startup pruning.
            await setup.IdempotencyRecords.AddAsync(
                Domain.Idempotency.IdempotencyRecord.Create("stale-key", "op", "hash", "{}", 201, DateTimeOffset.UtcNow - TimeSpan.FromHours(30), TimeSpan.FromHours(24)));

            await setup.SaveChangesAsync();
        }

        var factoryOptions = new DbContextOptionsBuilder<SharpAgentDbContext>()
            .UseSqlite(_database.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;
        var factory = new DbContextFactoryStub(factoryOptions);
        await new DbInitializer(factory, new NullWorktreeService()).InitializeAsync();

        await using (var verify = _database.OpenContext())
        {
            var session = await verify.Sessions
                .Include(static candidate => candidate.Runs)
                .SingleAsync(candidate => candidate.Id == sessionId);

            Assert.Equal(SessionStatus.Interrupted, session.Status);
            Assert.Null(session.ActiveRunId);

            var run = session.Runs.Single(candidate => candidate.Id == runId);
            Assert.Equal(RunStatus.Interrupted, run.Status);
            Assert.Contains("restarted", run.StopReason, StringComparison.Ordinal);

            var leases = await verify.RunLeases.ToListAsync();
            var lease = Assert.Single(leases);
            Assert.NotNull(lease.ReleasedAtUtc); // the sweep released the abandoned lease

            var statusEvent = await verify.AuditEvents
                .Where(static e => e.Type == Domain.Auditing.AuditEventTypes.Status)
                .SingleAsync();
            Assert.Equal(1, statusEvent.Sequence); // watermark advanced exactly once

            Assert.False(await verify.IdempotencyRecords.AnyAsync(static r => r.Key == "stale-key"));
        }
    }

    [Fact]
    public async Task Healthy_databases_pass_through_the_sweep_untouched()
    {
        await _database.InitializeAsync();

        await using (var setup = _database.OpenContext())
        {
            var session = Domain.Sessions.Session.CreateNew("ws", "t", SessionMode.Plan, "m", "p", Now);
            await setup.Sessions.AddAsync(session);
            await setup.SaveChangesAsync();
        }

        var factory = new DbContextFactoryStub(
            new DbContextOptionsBuilder<SharpAgentDbContext>()
                .UseSqlite(_database.ConnectionString)
                .Options);

        var exception = await Record.ExceptionAsync(
            () => new DbInitializer(factory, new NullWorktreeService()).InitializeAsync());

        Assert.Null(exception);
    }

    public void Dispose() => _database.Dispose();
}

internal sealed class DbContextFactoryStub(DbContextOptions<SharpAgentDbContext> options)
    : IDbContextFactory<SharpAgentDbContext>
{
    public SharpAgentDbContext CreateDbContext() => new(options);

    public Task<SharpAgentDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}







