using Microsoft.EntityFrameworkCore;
using SharpAgent.Domain.Idempotency;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Todos;
using SharpAgent.Infrastructure.Persistence;
using SharpAgent.Infrastructure.Tests.Support;
using SharpAgent.Infrastructure.Workspaces;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Infrastructure.Tests.Persistence;

public sealed class RepositoryBehaviorTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    private static readonly DateTimeOffset Now = new(2026, 8, 23, 17, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Catalog_repositories_round_trip_and_order()
    {
        await _database.InitializeAsync();

        await using (var writer = _database.OpenContext())
        {
            var zProfile = ModelProfile.Register(ProviderKind.DeepSeek, "Zulu", "z", EndpointKind.ChatCompletions, Now);
            var aProfile = ModelProfile.Register(ProviderKind.Fake, "Alpha", "a", EndpointKind.None, Now);
            aProfile.Enable(Now);
            await writer.ModelProfiles.AddRangeAsync([zProfile, aProfile]);
            await writer.PolicyProfiles.AddRangeAsync(
            [
                PolicyProfile.Define("default-controlled", 45, 40, 5m, 10, Now),
                PolicyProfile.Define("quick-plan", 10, 12, 1m, 5, Now),
            ]);
            await writer.SaveChangesAsync();
        }

        await using var context = _database.OpenContext();
        var profiles = new EfModelProfileRepository(context);
        var policies = new EfPolicyProfileRepository(context);

        Assert.Equal(["Alpha", "Zulu"], (await profiles.ListAsync(CancellationToken.None)).Select(static p => p.DisplayName));
        Assert.Null(await profiles.FindAsync("model_missing", CancellationToken.None));

        Assert.Equal(["default-controlled", "quick-plan"], (await policies.ListAsync(CancellationToken.None)).Select(static p => p.Name));
        Assert.NotNull(await policies.FindAsync((await policies.ListAsync(CancellationToken.None))[0].Id, CancellationToken.None));
    }

    [Fact]
    public async Task Todo_repository_lists_by_session_in_sequence_order()
    {
        await _database.InitializeAsync();

        await using (var writer = _database.OpenContext())
        {
            var session = Domain.Sessions.Session.CreateNew("ws", "t", SessionMode.Plan, "m", "p", Now);
            var run = session.BeginRun(Now);
            await writer.Sessions.AddAsync(session);
            await writer.TodoItems.AddRangeAsync(
            [
                TodoItem.Create(session.Id, run.Id, 2, "second", Now),
                TodoItem.Create(session.Id, run.Id, 1, "first", Now),
                TodoItem.Create(session.Id, run.Id, 3, "third", Now),
            ]);
            await writer.SaveChangesAsync();

            var repository = new EfTodoRepository(writer);
            var todos = await repository.ListBySessionAsync(session.Id, CancellationToken.None);

            Assert.Equal(["first", "second", "third"], todos.Select(static todo => todo.Text));
        }
    }

    [Fact]
    public async Task Lease_repository_releases_and_reports_unreleased_leases()
    {
        await _database.InitializeAsync();

        string sessionId;
        await using (var setup = _database.OpenContext())
        {
            var session = Domain.Sessions.Session.CreateNew("ws", "t", SessionMode.Plan, "m", "p", Now);
            sessionId = session.Id;
            await setup.Sessions.AddAsync(session);

            var leases = new EfRunLeaseRepository(setup);
            await leases.AddAsync(RunLease.Acquire(sessionId, "run_a", Now), CancellationToken.None);
            await setup.SaveChangesAsync();

            // A second live lease for the same session violates the partial index.
            await leases.AddAsync(RunLease.Acquire(sessionId, "run_b", Now.AddMinutes(1)), CancellationToken.None);
            await Assert.ThrowsAsync<DbUpdateException>(() => setup.SaveChangesAsync());

            // Release the first lease through the port.
            await leases.ReleaseForRunAsync("run_a", Now.AddMinutes(2), CancellationToken.None);
            await setup.SaveChangesAsync();
        }

        await using (var verify = _database.OpenContext())
        {
            var repository = new EfRunLeaseRepository(verify);

            var live = await repository.FindActiveBySessionAsync(sessionId, CancellationToken.None);
            Assert.NotNull(live);
            Assert.Equal("run_b", live!.RunId);

            Assert.Single(await repository.FindUnreleasedAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Idempotency_store_prunes_only_expired_records()
    {
        await _database.InitializeAsync();

        await using var context = _database.OpenContext();
        var store = new EfIdempotencyStore(context);

        await store.AddAsync(IdempotencyRecord.Create("fresh", "op", "h1", "{}", 201, Now, TimeSpan.FromHours(24)), CancellationToken.None);
        await store.AddAsync(IdempotencyRecord.Create("stale", "op", "h2", "{}", 201, Now - TimeSpan.FromHours(48), TimeSpan.FromHours(24)), CancellationToken.None);
        await context.SaveChangesAsync();

        var deleted = await store.DeleteExpiredAsync(Now, CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.NotNull(await store.FindAsync("fresh", CancellationToken.None));
        Assert.Null(await store.FindAsync("stale", CancellationToken.None));
    }

    public void Dispose() => _database.Dispose();
}

public sealed class FileSystemRootValidatorTests
{
    [Fact]
    public void Relative_paths_are_rejected()
    {
        var validation = new FileSystemRootValidator().Validate("relative/path");

        Assert.False(validation.IsValid);
        Assert.Contains("absolute path", validation.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_directories_are_rejected_with_safe_messages()
    {
        var validation = new FileSystemRootValidator().Validate(@"C:\definitely\not\a\real\dir\sharpagent");

        Assert.False(validation.IsValid);
        Assert.Contains("does not exist", validation.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Real_directories_canonicalize_and_trim_trailing_separators()
    {
        using var workspace = TempWorkspace.Create();

        var validation = new FileSystemRootValidator().Validate(workspace.RootPath + Path.DirectorySeparatorChar);

        Assert.True(validation.IsValid);
        Assert.Equal(workspace.RootPath, validation.CanonicalRootPath);
    }
}



public sealed class FileSystemRootValidatorBlankTests
{
    [Fact]
    public void Blank_roots_are_rejected()
    {
        var validation = new FileSystemRootValidator().Validate("   ");

        Assert.False(validation.IsValid);
    }
}

public sealed class DbSetWiringTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    [Fact]
    public async Task Every_modeled_entity_set_resolves_against_sqlite()
    {
        await _database.InitializeAsync();

        await using var context = _database.OpenContext();

        Assert.Empty(await context.ApprovalRequests.ToListAsync());
        Assert.Empty(await context.ToolExecutions.ToListAsync());
        Assert.Empty(await context.ChangeSets.ToListAsync());
        Assert.Empty(await context.FileChanges.ToListAsync());
        Assert.Empty(await context.UsageRecords.ToListAsync());
    }

    public void Dispose() => _database.Dispose();
}
