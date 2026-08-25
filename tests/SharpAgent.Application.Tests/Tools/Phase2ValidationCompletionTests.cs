using SharpAgent.Application.Common;
using SharpAgent.Application.Sessions;
using SharpAgent.Application.Tests.Support;
using SharpAgent.Application.Tools;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Changes;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.TestKit.Fakes;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Application.Tests.Tools;

/// <summary>
/// Completes the defensive/validation branch coverage for the Phase 2 tool
/// surface: request validation arms, catalog guards, operator-input bounds,
/// and proposal evidence for deletions/large files.
/// </summary>
public sealed class Phase2ValidationCompletionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 11, 0, 0, TimeSpan.Zero);

    private readonly TempWorkspace _workspace = TempWorkspace.Create();

    [Fact]
    public void Catalog_rejects_blank_names_and_resolves_known_entries()
    {
        Assert.Throws<ArgumentException>(() => FocusedCommandCatalog.Default.TryResolve(string.Empty, out _));
        Assert.False(FocusedCommandCatalog.Default.TryResolve("curl", out _));
        Assert.True(FocusedCommandCatalog.Default.TryResolve("dotnet", out var dotnet));
        Assert.Equal("dotnet", dotnet.Executable);
        Assert.True(FocusedCommandCatalog.Default.TryResolve("powershell", ["Get-Date -Format o"], out var powershell));
        Assert.Equal("powershell.exe", powershell.Executable);
        Assert.False(FocusedCommandCatalog.Default.TryResolve("powershell", ["Get-Process"], out _));
        Assert.True(FocusedCommandCatalog.Default.TryResolve("bash", ["pwd"], out var bash));
        Assert.Equal("bash.exe", bash.Executable);
        Assert.False(FocusedCommandCatalog.Default.TryResolve("bash", ["rm -rf ."], out _));
    }

    [Fact]
    public async Task Create_request_validation_covers_every_blank_field()
    {
        var fixture = new SessionServiceFixture();

        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.CreateAsync(
            new CreateSessionRequest(string.Empty, "t", SessionMode.Plan, fixture.ModelProfileId, fixture.PolicyProfileId),
            "k-ws"));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.CreateAsync(
            new CreateSessionRequest(fixture.WorkspaceId, "t", SessionMode.Plan, string.Empty, fixture.PolicyProfileId),
            "k-model"));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Service.CreateAsync(
            new CreateSessionRequest(fixture.WorkspaceId, "t", SessionMode.Plan, fixture.ModelProfileId, string.Empty),
            "k-policy"));
    }

    [Fact]
    public async Task Proposals_against_finished_runs_are_conflicted()
    {
        var fixture = await NewStartedExecuteFixtureAsync();
        var tools = fixture.Tools!;

        // Finish the active run, then attempt a proposal against it.
        var finished = await fixture.Fixture.Service.GetAsync(fixture.SessionId!);
        var run = fixture.Fixture.Sessions.Snapshot.Single().Runs.Single();
        run.Fail("done", fixture.Clock.UtcNow);
        finished = await fixture.Fixture.Service.GetAsync(fixture.SessionId!);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => tools.ProposeAsync(new ToolProposal(
            fixture.SessionId!, fixture.RunId!, fixture.WorkspaceId!,
            ToolAction.ReadFile, RelativePath: "src.txt")));

        Assert.Equal("run_not_active", conflict.Code);
    }

    [Fact]
    public async Task Change_set_proposals_cover_deletions_and_large_files()
    {
        _workspace.WriteFile("src/obsolete.ts", "old");
        _workspace.WriteFile("src/huge.ts", string.Concat(Enumerable.Repeat("line\n", 120)));

        var fixture = await NewStartedExecuteFixtureAsync();
        var changeSet = await fixture.ChangeSets!.ProposeAsync(fixture.SessionId!,
        [
            new ProposeFileChange("src/deleted.ts", null, Delete: true),
            new ProposeFileChange("src/huge.ts", string.Concat(Enumerable.Repeat("line\n", 120)), Delete: false),
        ]);

        Assert.Equal(2, changeSet.Files.Count);

        var huge = changeSet.Files.Single(static file => file.RelativePath == "src/huge.ts");
        Assert.Contains("more lines", huge.DiffText, StringComparison.Ordinal);

        var deleted = changeSet.Files.Single(static file => file.RelativePath == "src/deleted.ts");
        Assert.Null(deleted.DiffText);
    }

    [Fact]
    public async Task Pending_list_tolerates_corrupt_path_documents_and_long_comments()
    {
        var fixture = await NewStartedExecuteFixtureAsync();

        // Seed an approval whose AffectedPathsJson is corrupt + exercise comment truncation.
        var approval = ApprovalRequest.Create(
            fixture.RunId!, fixture.SessionId!, "fp-corrupt", "apply_patch",
            "[]", "not-json", null, Now, Now.AddMinutes(10),
            requestJson: "{}");
        await fixture.Fixture.Approvals.AddAsync(approval, CancellationToken.None);

        var pending = await fixture.Approvals.ListPendingAsync(fixture.SessionId!);
        Assert.Single(pending, dto => dto.Id == approval.Id);
        Assert.Empty(pending.Single(dto => dto.Id == approval.Id).AffectedPaths);

        var longComment = new string('c', 400);
        var outcome = await fixture.Approvals.ResolveAsync(
            approval.Id,
            new ResolveApprovalRequest(ApprovalDecision.Deny, longComment),
            $"deny-{Guid.NewGuid():N}");

        Assert.Equal("Denied", outcome.ApprovalStatus); // deny path completes; truncation exercised
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
        var changeSets = new ChangeSetService(
            fixture.ChangeSets, fixture.Sessions, fixture.Workspaces,
            _resolver, _files, fixture.Clock);

        var created = await fixture.Service.CreateAsync(
            new CreateSessionRequest(
                fixture.WorkspaceId, "p2 completion", SessionMode.Execute,
                fixture.ModelProfileId, fixture.PolicyProfileId),
            $"create-{Guid.NewGuid():N}");
        var started = await fixture.Service.StartOrResumeAsync(
            created.Id, new StartRunRequest(null, null), $"run-{Guid.NewGuid():N}");

        return new FixtureBundle
        {
            Fixture = fixture,
            Fakes = fakes,
            Tools = tools,
            Approvals = approvals,
            ChangeSets = changeSets,
            SessionId = started.Session.Id,
            RunId = started.Run.Id,
            WorkspaceId = started.Session.WorkspaceId,
        };
    }

    private sealed class FixtureBundle
    {
        public required SessionServiceFixture Fixture { get; init; }
        public required RecordingWorkspaceFakes Fakes { get; init; }
        public required WorkspaceToolService Tools { get; init; }
        public required ApprovalsService Approvals { get; init; }
        public FakeClock Clock => Fixture.Clock;
        public required ChangeSetService ChangeSets { get; init; }
        public string? SessionId { get; set; }
        public string? RunId { get; set; }
        public string? WorkspaceId { get; set; }
    }

    private readonly RecordingPathResolver _resolver = new();

    private readonly RecordingFileAccess _files;

    public Phase2ValidationCompletionTests()
    {
        _files = new RecordingFileAccess(_workspace.RootPath);
    }

    public void Dispose() => _workspace.Dispose();
}


