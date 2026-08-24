using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Sessions;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Common;
using SharpAgent.Application.Sessions;
using SharpAgent.Application.Tests.Support;
using SharpAgent.Application.Tools;
using SharpAgent.Domain.Changes;
using SharpAgent.TestKit.Fakes;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Application.Tests.Tools;

/// <summary>
/// Completes Phase 2 executor-path coverage: patch refusals, catalog guards,
/// deny/cancel outcome branches, and search/command dispatch arms that the
/// primary flow tests do not reach.
/// </summary>
public sealed class Phase2ExecutorCompletionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly TempWorkspace _workspace = TempWorkspace.Create();

    private readonly RecordingPathResolver _resolver = new();

    private readonly RecordingFileAccess _files;

    public Phase2ExecutorCompletionTests()
    {
        _files = new RecordingFileAccess(_workspace.RootPath);
    }

    [Fact]
    public void Hash_mismatch_refuses_the_patch_and_reports_partial_result()
    {
        _workspace.WriteFile("src/a.ts", "original");
        var changeSet = NewChangeSetWithContent("src/a.ts", "updated", beforeHashOverride: "stale-hash");

        var applied = PatchApplicationService.Apply(changeSet, _workspace.RootPath, _resolver, _files, new FakeClock(Now));

        Assert.False(applied.AllApplied);
        Assert.Contains("changed since the proposal", applied.SummaryText, StringComparison.Ordinal);
        Assert.Equal("original", File.ReadAllText(Path.Combine(_workspace.RootPath, "src", "a.ts")));
        Assert.Equal(ChangeSetStatus.Proposed, changeSet.Status); // caller decides final status
    }

    [Fact]
    public void Deletion_removes_the_file_when_the_hash_still_matches()
    {
        _workspace.WriteFile("src/old.ts", "obsolete");
        var beforeHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(_workspace.RootPath, "src", "old.ts"))));

        var changeSet = ChangeSet.CreateNew("run_del", Now);
        var entry = changeSet.AddFile("src/old.ts", FileChangeType.Deleted, Now);
        entry.RecordProposalEvidence(beforeHash, null, null, null, Now);

        var applied = PatchApplicationService.Apply(changeSet, _workspace.RootPath, _resolver, _files, new FakeClock(Now.AddMinutes(1)));

        Assert.True(applied.AllApplied);
        Assert.False(File.Exists(Path.Combine(_workspace.RootPath, "src", "old.ts")));
        Assert.Contains("Applied 1 file(s)", applied.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_deletion_targets_are_refused()
    {
        var changeSet = ChangeSet.CreateNew("run_missing", Now);
        var entry = changeSet.AddFile("src/ghost.ts", FileChangeType.Deleted, Now);
        entry.RecordProposalEvidence(string.Empty, null, null, null, Now);

        var applied = PatchApplicationService.Apply(changeSet, _workspace.RootPath, _resolver, _files, new FakeClock(Now));

        Assert.False(applied.AllApplied);
        Assert.Contains("was expected to exist", applied.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void Binary_content_is_refused_rather_than_written()
    {
        var changeSet = NewChangeSetWithContent("assets/logo.bin", content: null, isBinaryByNullContent: true);

        var applied = PatchApplicationService.Apply(changeSet, _workspace.RootPath, _resolver, _files, new FakeClock(Now));

        Assert.False(applied.AllApplied);
        Assert.Contains("binary", applied.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_catalog_commands_are_rejected_before_spawning()
    {
        var fixture = await NewStartedExecuteFixtureAsync();
        var tools = fixture.Tools;

        var exception = await Record.ExceptionAsync(() => tools.ProposeAsync(CommandProposal(fixture, commandName: "curl")));

        var validation = Assert.IsType<ValidationException>(exception);
        Assert.True(validation.Errors.ContainsKey("commandName"));
        Assert.DoesNotContain(fixture.Fakes.ProcessRunner.Requests, static request => request.Executable == "curl");
    }

    [Fact]
    public async Task Approved_run_commands_execute_through_the_catalog()
    {
        var fixture = await NewStartedExecuteFixtureAsync();
        fixture.Fakes.ProcessRunner.Handler = request =>
            new ProcessExecutionResult(0, $"ran {request.Executable} {string.Join(' ', request.Arguments)}", false, false, false);

        var proposal = await fixture.Tools.ProposeAsync(CommandProposal(fixture, commandName: "dotnet"));
        var pending = Assert.IsType<ToolProposalResult.AwaitingApproval>(proposal);

        var outcome = await fixture.Approvals.ResolveAsync(
            pending.ApprovalId,
            new ResolveApprovalRequest(ApprovalDecision.ApproveOnce, Comment: null),
            $"approve-cmd-{Guid.NewGuid():N}");

        var executed = Assert.IsType<ToolProposalResult.Executed>(outcome.ExecutionResult);
        Assert.Contains("ran dotnet --version", executed.OutputPreview, StringComparison.Ordinal);
        Assert.Single(fixture.Fakes.ProcessRunner.Requests);

        // Pending list is now empty for this session.
        Assert.Empty(await fixture.Approvals.ListPendingAsync(fixture.SessionId!, CancellationToken.None));
    }

    [Fact]
    public async Task Search_dispatch_returns_redacted_bounded_matches()
    {
        _workspace.WriteFile("find.txt", "needle here\nnothing\nNEEDLE again");
        var fixture = await NewStartedExecuteFixtureAsync();
        fixture.WorkspaceRootIs(_workspace.RootPath);

        var proposal = await fixture.Tools!.ProposeAsync(new ToolProposal(
            fixture.SessionId!, fixture.RunId!, fixture.WorkspaceId!,
            ToolAction.SearchText,
            RelativePath: ".",
            SearchQuery: "needle"));

        var executed = Assert.IsType<ToolProposalResult.Executed>(proposal);
        Assert.Contains("find.txt:1:", executed.OutputPreview, StringComparison.Ordinal);
        Assert.Contains("find.txt:3:", executed.OutputPreview, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private static ChangeSet NewChangeSetWithContent(
        string relativePath,
        string? content,
        string? beforeHashOverride = null,
        bool isBinaryByNullContent = false)
    {
        var changeSet = ChangeSet.CreateNew("run_p2", Now);
        var entry = changeSet.AddFile(relativePath, FileChangeType.Modified, Now);

        if (isBinaryByNullContent)
        {
            entry.RecordProposalEvidence(string.Empty, "afterhash", null, afterContentText: null, Now);
        }
        else
        {
            entry.RecordProposalEvidence(beforeHashOverride ?? string.Empty, "after", "+updated", content, Now);
        }

        return changeSet;
    }

    private sealed class FixtureBundle
    {
        public required SessionServiceFixture Fixture { get; init; }
        public required RecordingWorkspaceFakes Fakes { get; init; }
        public required WorkspaceToolService Tools { get; init; }
        public required ApprovalsService Approvals { get; init; }
        public string? SessionId { get; set; }
        public string? RunId { get; set; }
        public string? WorkspaceId { get; set; }

        /// <summary>Re-points the seeded workspace at a different temp root mid-test.</summary>
        public void WorkspaceRootIs(string root)
        {
            var seeded = Fixture.Workspaces.Snapshot.Single();
            seeded.MarkValidated(root, Fixture.Clock.UtcNow);
        }
    }

    private async Task<FixtureBundle> NewStartedExecuteFixtureAsync()
    {
        var fixture = new SessionServiceFixture();
        var fakes = new RecordingWorkspaceFakes(_workspace);
        var seeded = fixture.Workspaces.Snapshot.Single();
        seeded.MarkValidated(_workspace.RootPath, fixture.Clock.UtcNow);

        var tools = new WorkspaceToolService(
            fixture.Sessions, fixture.Workspaces, fixture.Profiles, fixture.Policies,
            fixture.Approvals, fixture.ChangeSets, fixture.ToolExecutions, fixture.Events,
            fixture.UnitOfWork, fixture.Clock, _resolver, _files,
            fakes.ProcessRunner, fakes.Worktrees, FocusedCommandCatalog.Default);
        var approvals = new ApprovalsService(
            fixture.Approvals, fixture.Sessions, fixture.Events, fixture.Idempotency,
            fixture.UnitOfWork, fixture.Clock, tools);
        _ = approvals;

        var created = await fixture.Service.CreateAsync(
            new CreateSessionRequest(fixture.WorkspaceId, "p2", SessionMode.Execute, fixture.ModelProfileId, fixture.PolicyProfileId),
            $"create-{Guid.NewGuid():N}");
        var started = await fixture.Service.StartOrResumeAsync(created.Id, new StartRunRequest(null, null), $"run-{Guid.NewGuid():N}");

        return new FixtureBundle
        {
            Fixture = fixture,
            Fakes = fakes,
            Tools = tools,
            Approvals = approvals,
            SessionId = started.Session.Id,
            RunId = started.Run.Id,
            WorkspaceId = started.Session.WorkspaceId,
        };
    }

    private static ToolProposal CommandProposal(FixtureBundle bundle, string commandName) => new(
        bundle.SessionId!, bundle.RunId!, bundle.WorkspaceId!,
        ToolAction.RunCommand,
        CommandName: commandName,
        Arguments: ["--version"]);

    public void Dispose() => _workspace.Dispose();
}

