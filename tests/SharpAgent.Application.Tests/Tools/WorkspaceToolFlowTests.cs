using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Auditing;
using SharpAgent.Application.Sessions;
using SharpAgent.Application.Tests.Support;
using SharpAgent.TestKit.Workspaces;
using SharpAgent.Application.Common;
using SharpAgent.Application.Tools;
using SharpAgent.Domain.Changes;
using SharpAgent.Domain.Sessions;
using SharpAgent.TestKit.Fakes;
using Xunit;

namespace SharpAgent.Application.Tests.Tools;

/// <summary>
/// End-to-end tool flow over recording fakes: reads execute and are audited,
/// Plan-mode proposals never reach executors, patches and commands each require
/// their own single-use approval (AC-01/AC-02 seeds at application level).
/// </summary>
public sealed class WorkspaceToolFlowTests : IDisposable
{
    private readonly TempWorkspace _workspace = TempWorkspace.Create();

    private readonly SessionServiceFixture _fixture;

    private readonly RecordingWorkspaceFakes _fakes;

    private readonly ChangeSetService _changeSets;

    private readonly ApprovalsService _approvals;

    public WorkspaceToolFlowTests()
    {
        _fixture = new SessionServiceFixture();
        _fakes = new RecordingWorkspaceFakes(_workspace);
        // Point the seeded workspace at the real temp directory for this test.
        var seeded = _fixture.Workspaces.Snapshot.Single();
        seeded.MarkValidated(_workspace.RootPath, _fixture.Clock.UtcNow);

        _changeSets = new ChangeSetService(
            _fixture.ChangeSets,
            _fixture.Sessions,
            _fixture.Workspaces,
            _fakes.PathResolver,
            _fakes.FileAccess,
            _fixture.Clock);

        var tools = new WorkspaceToolService(
            _fixture.Sessions,
            _fixture.Workspaces,
            _fixture.Profiles,
            _fixture.Policies,
            _fixture.Approvals,
            _fixture.ChangeSets,
            _fixture.ToolExecutions,
            _fixture.Events,
            _fixture.UnitOfWork,
            _fixture.Clock,
            _fakes.PathResolver,
            _fakes.FileAccess,
            _fakes.ProcessRunner,
            _fakes.Worktrees,
            FocusedCommandCatalog.Default);

        _approvals = new ApprovalsService(
            _fixture.Approvals,
            _fixture.Sessions,
            _fixture.Events,
            _fixture.Idempotency,
            _fixture.UnitOfWork,
            _fixture.Clock,
            tools);

        Tools = tools;
    }

    private WorkspaceToolService Tools { get; }

    [Fact]
    public async Task A_read_inside_the_boundary_executes_and_is_audited()
    {
        _workspace.WriteFile("src/app.cs", "Console.WriteLine(nameof(ReadFile));");
        var session = await StartExecuteSessionAsync();

        var result = await Tools.ProposeAsync(new ToolProposal(
            session.Id, session.ActiveRunId!, session.WorkspaceId, ToolAction.ReadFile, RelativePath: "src/app.cs"));

        var executed = Assert.IsType<ToolProposalResult.Executed>(result);
        Assert.Contains("ReadFile", executed.OutputPreview, StringComparison.Ordinal);
        Assert.Equal(1, (await _fixture.Events.ReplayAsync(session.Id, CancellationToken.None)).Count(static e => e.Type == AuditEventTypes.ToolCompleted));

        // Read-only work never created a worktree.
        Assert.Equal(0, _fakes.Worktrees.CreateCount);
    }

    [Fact]
    public async Task Write_and_edit_are_approval_gated_and_apply_only_in_the_run_worktree()
    {
        _workspace.WriteFile("src/app.cs", "answer = 41;");
        var session = await StartExecuteSessionAsync();

        var pendingWrite = Assert.IsType<ToolProposalResult.AwaitingApproval>(await Tools.ProposeAsync(new ToolProposal(
            session.Id,
            session.ActiveRunId!,
            session.WorkspaceId,
            ToolAction.WriteFile,
            RelativePath: "src/new.cs",
            Content: "answer = 42;")));

        Assert.False(File.Exists(Path.Combine(_workspace.RootPath, "src", "new.cs")));
        var writeResolution = await _approvals.ResolveAsync(
            pendingWrite.ApprovalId,
            new ResolveApprovalRequest(ApprovalDecision.ApproveOnce, null),
            $"approve-write-{Guid.NewGuid():N}");
        Assert.IsType<ToolProposalResult.Executed>(writeResolution.ExecutionResult);
        Assert.Contains("answer = 42", File.ReadAllText(Path.Combine(_fakes.Worktrees.LastCreatedPath!, "src", "new.cs")));

        var pendingEdit = Assert.IsType<ToolProposalResult.AwaitingApproval>(await Tools.ProposeAsync(new ToolProposal(
            session.Id,
            session.ActiveRunId!,
            session.WorkspaceId,
            ToolAction.EditFile,
            RelativePath: "src/app.cs",
            OldText: "41",
            NewText: "43")));
        var editResolution = await _approvals.ResolveAsync(
            pendingEdit.ApprovalId,
            new ResolveApprovalRequest(ApprovalDecision.ApproveOnce, null),
            $"approve-edit-{Guid.NewGuid():N}");
        Assert.IsType<ToolProposalResult.Executed>(editResolution.ExecutionResult);
        Assert.Contains("answer = 43", File.ReadAllText(Path.Combine(_fakes.Worktrees.LastCreatedPath!, "src", "app.cs")));
        Assert.Contains("answer = 41", File.ReadAllText(Path.Combine(_workspace.RootPath, "src", "app.cs")));
    }

    [Fact]
    public async Task Plan_mode_proposals_never_reach_the_executors()
    {
        var planSession = await StartSessionAsync(SessionMode.Plan);

        foreach (var action in new[] { ToolAction.ApplyPatch, ToolAction.RunCommand })
        {
            var proposal = action == ToolAction.RunCommand
                ? new ToolProposal(planSession.Id, planSession.ActiveRunId!, planSession.WorkspaceId, action, CommandName: "dotnet", Arguments: ["--version"])
                : new ToolProposal(planSession.Id, planSession.ActiveRunId!, planSession.WorkspaceId, action, RelativePath: "src/app.cs", ChangeSetId: "chg_none");

            var result = await Tools.ProposeAsync(proposal);

            Assert.IsType<ToolProposalResult.Denied>(result);
        }

        Assert.Equal(0, _fakes.ExecutorCalls);      // AC-07 guard: no executor call occurred
        Assert.Equal(0, _fakes.Worktrees.CreateCount);
        Assert.Empty(_fixture.ToolExecutions.Snapshot);
    }

    [Fact]
    public async Task Traversal_targets_are_denied_before_any_executor_call()
    {
        var session = await StartExecuteSessionAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => Tools.ProposeAsync(new ToolProposal(
            session.Id, session.ActiveRunId!, session.WorkspaceId, ToolAction.ReadFile, RelativePath: "../../etc/passwd")));

        Assert.Equal(1, _fakes.PathResolver.CallCount);  // resolution ran...
        Assert.Equal(0, _fakes.ExecutorCalls);           // ...and nothing else did
    }

    [Fact]
    public async Task Patch_then_focused_test_each_require_a_distinct_approval_once()
    {
        _workspace.WriteFile("src/lib.ts", "export const answer = 41;");
        var session = await StartExecuteSessionAsync();

        // Propose the patch against current state.
        var changeSet = await _changeSets.ProposeAsync(session.Id,
        [
            new ProposeFileChange("src/lib.ts", "export const answer = 42;", Delete: false),
        ]);

        var patchProposal = await Tools.ProposeAsync(new ToolProposal(
            session.Id, session.ActiveRunId!, session.WorkspaceId,
            ToolAction.ApplyPatch, ChangeSetId: changeSet.Id));
        var pendingPatch = Assert.IsType<ToolProposalResult.AwaitingApproval>(patchProposal);

        // Nothing applied yet; the base file is untouched.
        Assert.DoesNotContain("answer = 42", File.ReadAllText(Path.Combine(_workspace.RootPath, "src", "lib.ts")));

        var resolved = await _approvals.ResolveAsync(
            pendingPatch.ApprovalId,
            new ResolveApprovalRequest(ApprovalDecision.ApproveOnce, Comment: null),
            $"approve-patch-{Guid.NewGuid():N}");

        Assert.IsType<ToolProposalResult.Executed>(resolved.ExecutionResult);

        // The registered base checkout is NOT the patch target; the run worktree is.
        var basePath = Path.Combine(_workspace.RootPath, "src", "lib.ts");
        Assert.Contains("answer = 41", File.ReadAllText(basePath));
        Assert.NotNull(_fakes.Worktrees.LastCreatedPath);
        var worktreeText = File.ReadAllText(Path.Combine(_fakes.Worktrees.LastCreatedPath!, "src", "lib.ts"));
        Assert.Contains("answer = 42", worktreeText);

        // Replaying the same decision is rejected: the approval was consumed.
        await Assert.ThrowsAsync<ConflictException>(() => _approvals.ResolveAsync(
            pendingPatch.ApprovalId,
            new ResolveApprovalRequest(ApprovalDecision.ApproveOnce, Comment: null),
            $"approve-again-{Guid.NewGuid():N}"));

        // A focused test command requires its OWN approval.
        var commandProposal = await Tools.ProposeAsync(new ToolProposal(
            session.Id, session.ActiveRunId!, session.WorkspaceId,
            ToolAction.RunCommand, CommandName: "dotnet", Arguments: ["test"]));
        var pendingCommand = Assert.IsType<ToolProposalResult.AwaitingApproval>(commandProposal);

        var commandResolved = await _approvals.ResolveAsync(
            pendingCommand.ApprovalId,
            new ResolveApprovalRequest(ApprovalDecision.Deny, "Not yet"),
            $"deny-cmd-{Guid.NewGuid():N}");

        Assert.Null(commandResolved.ExecutionResult);   // denied => nothing executed
        Assert.DoesNotContain(_fakes.ProcessRunner.Requests, static request => request.Executable == "dotnet"); // denied => never spawned
    }

    [Fact]
    public async Task Expired_approvals_cannot_be_resolved()
    {
        _workspace.WriteFile("a.txt", "one");
        var session = await StartExecuteSessionAsync();

        var changeSet = await _changeSets.ProposeAsync(session.Id,
        [
            new ProposeFileChange("a.txt", "two", Delete: false),
        ]);
        var pending = Assert.IsType<ToolProposalResult.AwaitingApproval>(await Tools.ProposeAsync(new ToolProposal(
            session.Id, session.ActiveRunId!, session.WorkspaceId, ToolAction.ApplyPatch, ChangeSetId: changeSet.Id)));

        _fixture.Clock.Advance(TimeSpan.FromMinutes(11)); // policy expiry is 10 minutes

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => _approvals.ResolveAsync(
            pending.ApprovalId,
            new ResolveApprovalRequest(ApprovalDecision.ApproveOnce, Comment: null),
            "late-key"));

        Assert.Equal("approval_expired", conflict.Code);
    }

    [Fact]
    public async Task Changed_workspace_state_invalidates_the_fingerprint_before_execution()
    {
        _workspace.WriteFile("b.txt", "before");
        var session = await StartExecuteSessionAsync();

        var changeSet = await _changeSets.ProposeAsync(session.Id,
        [
            new ProposeFileChange("b.txt", "after", Delete: false),
        ]);
        var pending = Assert.IsType<ToolProposalResult.AwaitingApproval>(await Tools.ProposeAsync(new ToolProposal(
            session.Id, session.ActiveRunId!, session.WorkspaceId, ToolAction.ApplyPatch, ChangeSetId: changeSet.Id)));

        // Mutate the stored payload AFTER approval was requested.
        var approval = _fixture.Approvals.Snapshot.Single(candidate => candidate.Id == pending.ApprovalId);
        typeof(Domain.Approvals.ApprovalRequest)
            .GetProperty(nameof(Domain.Approvals.ApprovalRequest.RequestJson))!
            .SetValue(approval, """{"proposal":null,"targets":[],"patchContentHash":"tampered"}""");

        // Approve through the service; execution must refuse on fingerprint mismatch.
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => _approvals.ResolveAsync(
            pending.ApprovalId,
            new ResolveApprovalRequest(ApprovalDecision.ApproveOnce, Comment: null),
            "tampered-key"));

        Assert.Equal("approval_payload_invalid", conflict.Code);
        Assert.Contains("before", File.ReadAllText(Path.Combine(_workspace.RootPath, "b.txt")));
    }

    [Fact]
    public async Task Cancel_run_decision_stops_the_session()
    {
        _workspace.WriteFile("c.txt", "x");
        var session = await StartExecuteSessionAsync();

        var changeSet = await _changeSets.ProposeAsync(session.Id,
        [
            new ProposeFileChange("c.txt", "y", Delete: false),
        ]);
        var pending = Assert.IsType<ToolProposalResult.AwaitingApproval>(await Tools.ProposeAsync(new ToolProposal(
            session.Id, session.ActiveRunId!, session.WorkspaceId, ToolAction.ApplyPatch, ChangeSetId: changeSet.Id)));

        var outcome = await _approvals.ResolveAsync(
            pending.ApprovalId,
            new ResolveApprovalRequest(ApprovalDecision.CancelRun, Comment: null),
            "cancel-key");

        Assert.Equal(SessionStatus.Cancelled, outcome.SessionStatus);
        Assert.Null(outcome.ExecutionResult);
    }

    private async Task<SessionDto> StartSessionAsync(SessionMode mode)
    {
        var request = new CreateSessionRequest(
            WorkspaceId: _fixture.WorkspaceId,
            Task: "phase 2 flow",
            Mode: mode,
            ModelProfileId: _fixture.ModelProfileId,
            PolicyProfileId: _fixture.PolicyProfileId);

        var created = await _fixture.Service.CreateAsync(request, $"create-{Guid.NewGuid():N}");
        var started = await _fixture.Service.StartOrResumeAsync(
            created.Id, new StartRunRequest(null, null), $"run-{Guid.NewGuid():N}");

        return started.Session; // ActiveRunId now set
    }

    private async Task<SessionDto> StartExecuteSessionAsync() =>
        await StartSessionAsync(SessionMode.Execute);

    public void Dispose() => _workspace.Dispose();
}






