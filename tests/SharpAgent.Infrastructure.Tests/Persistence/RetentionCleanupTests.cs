using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Changes;
using SharpAgent.Domain.Idempotency;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Tools;
using SharpAgent.Domain.Workspaces;
using SharpAgent.Infrastructure.Persistence;
using SharpAgent.Infrastructure.Retention;
using SharpAgent.Infrastructure.Tests.Support;
using Xunit;

namespace SharpAgent.Infrastructure.Tests.Persistence;

public sealed class RetentionCleanupTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase database = SqliteTestDatabase.Create();
    private readonly string managedParent = Path.Combine(Path.GetTempPath(), "sharpagent-worktrees");
    private readonly string expiredWorktree;
    private readonly string registeredBase;

    public RetentionCleanupTests()
    {
        var suffix = Guid.NewGuid().ToString("N");
        expiredWorktree = Path.Combine(managedParent, "wt_phase7_expired_" + suffix);
        registeredBase = Path.Combine(managedParent, "wt_phase7_registered_" + suffix);
        Directory.CreateDirectory(expiredWorktree);
        Directory.CreateDirectory(registeredBase);
    }

    [Fact]
    public async Task Cleanup_prunes_transient_artifacts_but_preserves_audit_change_and_registered_base_evidence()
    {
        await database.InitializeAsync();

        await using (var writer = database.OpenContext())
        {
            var workspace = Workspace.Register("registered", registeredBase, Now.AddDays(-3));
            workspace.MarkValidated(registeredBase, Now.AddDays(-3));
            await writer.Workspaces.AddAsync(workspace);

            var session = Domain.Sessions.Session.CreateNew(
                workspace.Id,
                "retention",
                SessionMode.Execute,
                "model",
                "policy",
                Now.AddDays(-3));
            var expiredRun = session.BeginRun(Now.AddDays(-3));
            expiredRun.AssignEnvironment("wt_expired", expiredWorktree);
            session.CompleteActiveRun("completed", Now.AddDays(-2));

            var protectedRun = session.BeginRun(Now.AddDays(-2), resumeSourceRunId: expiredRun.Id);
            protectedRun.AssignEnvironment("wt_registered", registeredBase);
            session.CompleteActiveRun("completed", Now.AddDays(-2).AddMinutes(1));
            await writer.Sessions.AddAsync(session);

            await writer.IdempotencyRecords.AddRangeAsync(
            [
                IdempotencyRecord.Create(
                    "expired-key",
                    "operation",
                    "hash-expired",
                    "{}",
                    201,
                    Now.AddDays(-3),
                    TimeSpan.FromHours(24)),
                IdempotencyRecord.Create(
                    "fresh-key",
                    "operation",
                    "hash-fresh",
                    "{}",
                    201,
                    Now,
                    TimeSpan.FromHours(24)),
            ]);

            var output = ToolExecution.Start(
                expiredRun.Id,
                "read_file",
                PolicyOutcome.Allow,
                null,
                Now.AddDays(-3));
            output.Complete(0, "bounded output", false, true, Now.AddDays(-3).AddMinutes(1));
            await writer.ToolExecutions.AddAsync(output);

            var changeSet = ChangeSet.CreateNew(expiredRun.Id, Now.AddDays(-3));
            var fileChange = changeSet.AddFile("src/file.cs", FileChangeType.Modified, Now.AddDays(-3));
            fileChange.RecordProposalEvidence("before", "after", "-before\n+after", "after", Now.AddDays(-3));
            await writer.ChangeSets.AddAsync(changeSet);

            var sequence = session.ReserveNextEventSequence();
            await writer.AuditEvents.AddAsync(
                AuditEvent.Create(
                    session.Id,
                    expiredRun.Id,
                    sequence,
                    AuditEventTypes.ChangeDetected,
                    "{\"changeSetId\":\"retained\"}",
                    Now.AddDays(-3),
                    expiredRun.CorrelationId));

            await writer.SaveChangesAsync();
        }

        var factory = new DbContextFactoryStub(
            new DbContextOptionsBuilder<SharpAgentDbContext>()
                .UseSqlite(database.ConnectionString)
                .Options);
        var worktrees = new RecordingCleanupWorktreeService();
        var cleanup = new RetentionCleanupService(
            factory,
            worktrees,
            new RetentionOptions
            {
                WorktreeHours = 24,
                ToolOutputHours = 24,
            },
            NullLogger<RetentionCleanupService>.Instance);

        var result = await cleanup.CleanupAsync(Now);

        Assert.Equal(1, result.IdempotencyRecordsDeleted);
        Assert.Equal(1, result.ToolOutputPreviewsCleared);
        Assert.Equal(1, result.WorktreesRemoved);
        Assert.Equal(1, result.WorktreesSkipped);
        Assert.Equal([expiredWorktree], worktrees.RemovedPaths);
        Assert.True(Directory.Exists(registeredBase));

        await using var verify = database.OpenContext();
        Assert.Null(await verify.IdempotencyRecords.SingleOrDefaultAsync(record => record.Key == "expired-key"));
        Assert.NotNull(await verify.IdempotencyRecords.SingleOrDefaultAsync(record => record.Key == "fresh-key"));

        var persistedOutput = await verify.ToolExecutions.SingleAsync();
        Assert.Null(persistedOutput.OutputPreview);
        Assert.NotNull(await verify.ChangeSets.SingleOrDefaultAsync());
        Assert.NotNull(await verify.FileChanges.SingleOrDefaultAsync());
        Assert.NotNull(await verify.AuditEvents.SingleOrDefaultAsync());
    }

    public void Dispose()
    {
        database.Dispose();
        TryDelete(expiredWorktree);
        TryDelete(registeredBase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class RecordingCleanupWorktreeService : IGitWorktreeService
    {
        public List<string> RemovedPaths { get; } = [];

        public bool Exists(string worktreePath) => Directory.Exists(worktreePath);

        public Task<WorktreeInfo> CreateAsync(
            string baseRepositoryRoot,
            string runId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task RemoveAsync(WorktreeInfo worktree, CancellationToken cancellationToken)
        {
            RemovedPaths.Add(worktree.Path);
            Directory.Delete(worktree.Path, recursive: true);
            return Task.CompletedTask;
        }
    }
}

public sealed class RetentionOptionsTests
{
    [Fact]
    public void Configuration_values_are_parsed_and_missing_values_use_safe_defaults()
    {
        var options = RetentionOptions.FromConfiguration(
            new DictionaryConfiguration(
                new Dictionary<string, string?>
                {
                    ["Retention:WorktreeHours"] = "48",
                }));

        Assert.Equal(48, options.WorktreeHours);
        Assert.Equal(24 * 7, options.ToolOutputHours);
        Assert.Equal(TimeSpan.FromHours(48), options.WorktreeAge);
        Assert.Equal(TimeSpan.FromDays(7), options.ToolOutputAge);
    }

    [Theory]
    [InlineData("Retention:WorktreeHours", "0")]
    [InlineData("Retention:ToolOutputHours", "not-a-number")]
    public void Non_positive_or_invalid_values_are_rejected(string key, string value)
    {
        var configuration = new DictionaryConfiguration(new Dictionary<string, string?> { [key] = value });

        Assert.Throws<InvalidOperationException>(() => RetentionOptions.FromConfiguration(configuration));
    }

    [Fact]
    public void Null_configuration_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => RetentionOptions.FromConfiguration(null!));
    }

    private sealed class DictionaryConfiguration(IReadOnlyDictionary<string, string?> values) : IConfiguration
    {
        public string? this[string key]
        {
            get => values.TryGetValue(key, out var value) ? value : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => new NoChangeToken();

        public IConfigurationSection GetSection(string key) => new DictionaryConfigurationSection(key, values);
    }

    private sealed class DictionaryConfigurationSection(
        string key,
        IReadOnlyDictionary<string, string?> values) : IConfigurationSection
    {
        public string? this[string childKey]
        {
            get => values.TryGetValue($"{key}:{childKey}", out var value) ? value : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => new NoChangeToken();

        public IConfigurationSection GetSection(string childKey) =>
            new DictionaryConfigurationSection($"{key}:{childKey}", values);

        public string Key => key[(key.LastIndexOf(':') + 1)..];

        public string Path => key;

        public string? Value
        {
            get => values.TryGetValue(key, out var value) ? value : null;
            set => throw new NotSupportedException();
        }
    }

    private sealed class NoChangeToken : IChangeToken
    {
        public bool HasChanged => false;

        public bool ActiveChangeCallbacks => false;

        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) =>
            new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
