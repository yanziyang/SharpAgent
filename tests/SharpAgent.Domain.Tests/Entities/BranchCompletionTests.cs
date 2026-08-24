using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Changes;
using SharpAgent.Domain.Common;
using SharpAgent.Domain.Idempotency;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Tools;
using SharpAgent.Domain.Workspaces;
using Xunit;

namespace SharpAgent.Domain.Tests.Entities;

/// <summary>
/// Exercises defensive default/null arms so branch coverage reflects real decision
/// points rather than unreachable dead code (which was removed instead).
/// </summary>
public sealed class BranchCompletionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Change_set_summaries_accept_null()
    {
        var changeSet = ChangeSet.CreateNew("run_1", Now);
        changeSet.MarkApplied(summary: null, Now);
        Assert.Null(changeSet.Summary);

        var failed = ChangeSet.CreateNew("run_2", Now);
        failed.MarkFailed(null, Now);
        Assert.Equal(ChangeSetStatus.Failed, failed.Status);
    }

    [Fact]
    public void Idempotency_expiry_boundary_is_inclusive()
    {
        var record = IdempotencyRecord.Create("k", "op", "h", "{}", 201, Now, TimeSpan.FromHours(1));

        Assert.False(record.IsExpired(Now.AddMinutes(59)));
        Assert.True(record.IsExpired(Now.AddHours(1))); // exactly at expiry
    }

    [Fact]
    public void Unavailable_workspaces_tolerate_missing_messages()
    {
        var workspace = Workspace.Register("Demo", @"C:\repos\demo", Now);

        workspace.MarkUnavailable(string.Empty, Now.AddMinutes(1));

        Assert.Equal(WorkspaceStatus.Unavailable, workspace.Status);
        Assert.Null(workspace.ValidationMessage);
    }

    [Fact]
    public void Approvals_without_reasons_are_valid()
    {
        var approval = ApprovalRequest.Create(
            "run_1",
            "ses_1",
            "fp", "apply_patch", "summary", "[]", null,
            Now,
            Now.AddMinutes(10));

        Assert.Null(approval.Reason);
    }

    [Fact]
    public void Cancellation_requests_require_an_active_run_only()
    {
        var session = Session.CreateNew("ws", "t", SessionMode.Plan, "m", "p", Now);
        var run = session.BeginRun(Now.AddMinutes(1));

        run.RecordCancellationRequest(Now.AddMinutes(2));
        Assert.NotNull(run.CancelRequestedAtUtc);

        // The run keeps executing until cancellation completes it.
        Assert.Equal(RunStatus.Planning, run.Status);
    }

    [Fact]
    public void Tool_executions_start_running_with_their_policy_outcome()
    {
        var denied = ToolExecution.Start("run_1", "delete_file", PolicyOutcome.Deny, null, Now);

        Assert.Equal(PolicyOutcome.Deny, denied.PolicyOutcome);
        Assert.Equal(ToolExecutionStatus.Running, denied.Status);
        Assert.Null(denied.ApprovalId);
    }

    [Fact]
    public void Invalid_transitions_name_both_states()
    {
        var exception = new InvalidStateTransitionException("session", "draft", "completed");

        Assert.Contains("draft", exception.Message, StringComparison.Ordinal);
        Assert.Contains("completed", exception.Message, StringComparison.Ordinal);
    }
}


