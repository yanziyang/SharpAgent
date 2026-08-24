using SharpAgent.Application.Abstractions;
using SharpAgent.TestKit.Fakes;
using SharpAgent.Domain.Tools;
using SharpAgent.Application.Common;
using SharpAgent.Application.Tests.Support;
using SharpAgent.Application.Sessions;
using SharpAgent.Application.Tools;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Changes;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Application.Tests.Tools;

/// <summary>
/// Completes defensive-branch coverage for the Phase 2 executor: failing commands,
/// argument-less approvals, empty change sets, corrupt operator documents, and
/// approvals whose run vanished.
/// </summary>
public sealed class Phase2ExecutorEdgeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly TempWorkspace _workspace = TempWorkspace.Create();

    private readonly RecordingPathResolver _resolver = new();

    [Fact]
    public async Task Failing_commands_report_their_exit_code_through_the_gate()
    {
        var bundle = await NewStartedFixtureAsync();

        var proposal = await bundle.Tools!.ProposeAsync(Command(bundle, "dotnet", ["--no-such-flag"]));
        var pending = Assert.IsType<ToolProposalResult.AwaitingApproval>(proposal);

        bundle.Fakes!.ProcessRunner.Handler = request =>
            new ProcessExecutionResult(9, "simulated failure", false, false, false);

        var outcome = await bundle.Approvals!.ResolveAsync(
            pending.ApprovalId,
            new ResolveApprovalRequest(ApprovalDecision.ApproveOnce, Comment: null),
            $"approve-{Guid.NewGuid():N}");

        var executed = Assert.IsType<ToolProposalResult.Executed>(outcome.ExecutionResult);
        Assert.Contains("[exit 9]", executed.OutputPreview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Argument_less_approvals_execute_with_base_arguments_only()
    {
        var bundle = await NewStartedFixtureAsync();

        var proposal = await bundle.Tools!.ProposeAsync(
            new ToolProposal(bundle.SessionId!, bundle.RunId!, bundle.WorkspaceId!,
                ToolAction.RunCommand, CommandName: "dotnet"));

        var pending = Assert.IsType<ToolProposalResult.AwaitingApproval>(proposal);
        await bundle.Approvals!.ResolveAsync(
            pending.ApprovalId,
            new ResolveApprovalRequest(ApprovalDecision.ApproveOnce, Comment: null),
            $"approve-{Guid.NewGuid():N}");

        var request = Assert.Single(bundle.Fakes!.ProcessRunner.Requests);
        Assert.Equal("dotnet", request.Executable);
        Assert.Empty(request.Arguments); // no requested args -> base template args only
    }

    [Fact]
    public void Empty_change_sets_apply_to_nothing_and_report_failure()
    {
        var changeSet = ChangeSet.CreateNew("run_empty", Now);

        var clock = new FakeClock(Now);
        var applied = PatchApplicationService.Apply(changeSet, _workspace.RootPath, _resolver, _files, clock);

        Assert.False(applied.AllApplied);
        Assert.Empty(applied.AppliedFiles);
    }

    [Fact]
    public void Malformed_operator_rule_documents_fail_closed()
    {
        // "null" document deserializes to an empty rule dictionary -> defaults apply.
        var policy = PolicyProfile.Define(
            "null-rules", 10, 5, 1m, 5, Now, rulesJson: "null");

        var patchDecision = PolicyEvaluator.Evaluate(SessionMode.Execute, ReadProposal(), policy, ValidatedProfile());
        var applyDecision = PolicyEvaluator.Evaluate(SessionMode.Execute, PatchProposal(), policy, ValidatedProfile());

        Assert.Equal(PolicyOutcome.Allow, patchDecision.Outcome);
        Assert.Equal(PolicyOutcome.RequireApproval, applyDecision.Outcome);
    }

    [Fact]
    public async Task Approvals_whose_run_vanished_cannot_execute()
    {
        var fixture = await NewStartedFixtureAsync();

        var orphanPayload = new ApprovalStoredPayload(
            new ToolProposal(fixture.SessionId!, "run_vanished", fixture.WorkspaceId!,
                ToolAction.ReadFile, RelativePath: "src.txt"),
            [], PatchContentHash: string.Empty);
        var orphan = ApprovalRequest.Create(
            "run_vanished", fixture.SessionId!, "fp-orphan", "apply_patch",
            "orphan approval", "[]", null, Now, Now.AddMinutes(10),
            requestJson: System.Text.Json.JsonSerializer.Serialize(orphanPayload));
        await fixture.Fixture.Approvals.AddAsync(orphan, CancellationToken.None);
        orphan.Resolve(ApprovalDecision.ApproveOnce, fixture.Clock.UtcNow);

        var tools = fixture.Tools!;
        var conflict = await Assert.ThrowsAsync<ConflictException>(
            () => tools.ExecuteApprovedAsync(orphan.Id));

        Assert.Equal("no_active_run", conflict.Code);
    }

    [Fact]
    public async Task Pending_list_tolerates_null_path_documents()
    {
        var fixture = await NewStartedFixtureAsync();

        var approval = ApprovalRequest.Create(
            fixture.RunId!, fixture.SessionId!, "fp-null-paths", "apply_patch",
            "pending summary",
            "null", null, fixture.Clock.UtcNow, fixture.Clock.UtcNow.AddMinutes(10));
        await fixture.Fixture.Approvals.AddAsync(approval, CancellationToken.None);

        var pending = await fixture.Approvals.ListPendingAsync(fixture.SessionId!, CancellationToken.None);
        Assert.Empty(pending.Single(dto => dto.Id == approval.Id).AffectedPaths);
    }

    private static ToolProposal ReadProposal() => new(
        "ses", "run", "ws", ToolAction.ReadFile, RelativePath: "src.txt");

    private static ToolProposal PatchProposal() => new(
        "ses", "run", "ws", ToolAction.ApplyPatch, ChangeSetId: "chg_x");

    private static ModelProfile ValidatedProfile()
    {
        var profile = ModelProfile.Register(ProviderKind.Fake, "P", "id", EndpointKind.None, Now);
        profile.Enable(Now);
        profile.MarkValidated(new ProfileCapabilities(true, true, null, null, null), "ok", Now);
        return profile;
    }

    private sealed class Bundle
    {
        public required SessionServiceFixture Fixture { get; init; }
        public required RecordingWorkspaceFakes Fakes { get; init; }
        public required WorkspaceToolService Tools { get; init; }
        public required ApprovalsService Approvals { get; init; }
        public string? SessionId { get; set; }
        public string? RunId { get; set; }
        public string? WorkspaceId { get; set; }
        public FakeClock Clock => Fixture.Clock;
    }


    private RecordingFileAccess _files;

    public Phase2ExecutorEdgeTests()
    {
        _files = new RecordingFileAccess(_workspace.RootPath);
    }

    private async Task<Bundle> NewStartedFixtureAsync()
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

        _workspace.WriteFile("src.txt", "stable content");

        var created = await fixture.Service.CreateAsync(
            new CreateSessionRequest(fixture.WorkspaceId, "edges", SessionMode.Execute, fixture.ModelProfileId, fixture.PolicyProfileId),
            $"create-{Guid.NewGuid():N}");
        var started = await fixture.Service.StartOrResumeAsync(created.Id, new StartRunRequest(null, null), $"run-{Guid.NewGuid():N}");

        return new Bundle
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

    private static ToolProposal Command(Bundle bundle, string name, string[] args) => new(
        bundle.SessionId!, bundle.RunId!, bundle.WorkspaceId!,
        ToolAction.RunCommand, CommandName: name, Arguments: args);

    public void Dispose() => _workspace.Dispose();
}





