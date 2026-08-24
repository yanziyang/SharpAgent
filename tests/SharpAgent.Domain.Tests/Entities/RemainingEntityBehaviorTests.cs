using SharpAgent.Domain.Changes;
using SharpAgent.Domain.Common;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Todos;
using SharpAgent.Domain.Tools;
using SharpAgent.Domain.Usage;
using SharpAgent.Domain.Workspaces;
using Xunit;

namespace SharpAgent.Domain.Tests.Entities;

/// <summary>
/// Focused coverage for entity behaviors not yet exercised by application flows
/// (usage capture, tool cancellation, environment assignment, id factories).
/// </summary>
public sealed class UsageRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Usage_lifecycle_records_provider_facts()
    {
        var usage = UsageRecord.StartNew("run_1", "ses_1", "opencodego", "model_1", Now);

        Assert.Null(usage.InputTokens);
        Assert.Equal(0, usage.ContextCompactions);

        usage.Record(
            inputTokens: 1_234,
            outputTokens: 567,
            estimatedCostUsd: 0.0123m,
            latencyMs: 850,
            Now.AddMinutes(1));

        Assert.Equal(1_234, usage.InputTokens);
        Assert.Equal(567, usage.OutputTokens);
        Assert.Equal(0.0123m, usage.EstimatedCostUsd);
        Assert.Equal(850, usage.LatencyMs);
        Assert.Equal(Now.AddMinutes(1), usage.RecordedAtUtc);
    }

    [Fact]
    public void StartNew_requires_a_run()
    {
        Assert.Throws<ArgumentException>(
            () => UsageRecord.StartNew(string.Empty, "ses", "p", "m", Now));
    }
}

public sealed class ToolCancellationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Cancelled_executions_finalize_once()
    {
        var execution = ToolExecution.Start("run_1", "run_tests", PolicyOutcome.RequireApproval, "apr_1", Now);

        // Simulate the cooperative-cancel path without blocking the test.
        await Task.Yield();
        execution.MarkCancelled(Now.AddSeconds(5));

        Assert.Equal(ToolExecutionStatus.Cancelled, execution.Status);
        Assert.NotNull(execution.EndedAtUtc);
    }
}

public sealed class EnvironmentAssignmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Active_runs_record_their_execution_environment_once_started()
    {
        var session = Domain.Sessions.Session.CreateNew("ws", "t", SessionMode.Execute, "m", "p", Now);
        var run = session.BeginRun(Now.AddMinutes(1));

        run.AssignEnvironment("worktree-wt-42", @"C:\wt\42");

        Assert.Equal("worktree-wt-42", run.ExecutionEnvironmentId);

        Assert.Throws<ArgumentException>(() => run.AssignEnvironment(string.Empty, @"C:\wt\empty"));
    }

    [Fact]
    public void Terminal_runs_cannot_receive_an_environment()
    {
        var session = Domain.Sessions.Session.CreateNew("ws", "t", SessionMode.Execute, "m", "p", Now);
        var run = session.BeginRun(Now.AddMinutes(1));
        run.Fail("boom", Now.AddMinutes(2));

        Assert.Throws<InvalidStateTransitionException>(() => run.AssignEnvironment("wt-late", @"C:\wt\late"));
    }
}

public sealed class SessionFailurePathsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fail_and_interrupt_drive_the_session_to_matching_states()
    {
        var failSession = Domain.Sessions.Session.CreateNew("ws", "t", SessionMode.Plan, "m", "p", Now);
        var failRun = failSession.BeginRun(Now.AddMinutes(1));
        failSession.FailActiveRun("provider error", Now.AddMinutes(2));
        Assert.Equal(SessionStatus.Failed, failSession.Status);
        Assert.Equal(RunStatus.Failed, failRun.Status);

        var interruptSession = Domain.Sessions.Session.CreateNew("ws", "t", SessionMode.Plan, "m", "p", Now);
        var interruptRun = interruptSession.BeginRun(Now.AddMinutes(1));
        interruptSession.InterruptActiveRun("host restart", Now.AddMinutes(2));
        Assert.Equal(SessionStatus.Interrupted, interruptSession.Status);
        Assert.Equal(RunStatus.Interrupted, interruptRun.Status);
    }
}

public sealed class ChangeSetGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Guards_cover_run_and_path_references()
    {
        Assert.Throws<ArgumentException>(() => ChangeSet.CreateNew(string.Empty, Now));

        var changeSet = ChangeSet.CreateNew("run_1", Now);
        Assert.Throws<ArgumentException>(() => changeSet.AddFile(" ", FileChangeType.Added, Now));

        var added = changeSet.AddFile("src/new.ts", FileChangeType.Added, Now);
        Assert.False(added.IsBinary);
        Assert.Equal(FileChangeType.Added, added.ChangeType);
    }
}

public sealed class TodoEdgeTransitionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void In_progress_items_may_complete_or_return_to_pending()
    {
        var todo = TodoItem.Create("ses", "run", 1, "step", Now);

        todo.TransitionTo(TodoStatus.InProgress, Now.AddMinutes(1));
        todo.TransitionTo(TodoStatus.InProgress, Now.AddMinutes(2)); // no-op keeps timestamp
        var updatedAt = todo.UpdatedAtUtc;
        todo.TransitionTo(TodoStatus.Pending, Now.AddMinutes(3));
        Assert.NotEqual(updatedAt, todo.UpdatedAtUtc);

        todo.TransitionTo(TodoStatus.Completed, Now.AddMinutes(4));
        todo.TransitionTo(TodoStatus.Completed, Now.AddMinutes(5)); // no-op
        Assert.Equal(TodoStatus.Completed, todo.Status);
    }

    [Fact]
    public void Blank_updates_are_rejected()
    {
        var todo = TodoItem.Create("ses", "run", 1, "step", Now);

        Assert.Throws<ArgumentException>(() => todo.UpdateText("   ", Now.AddMinutes(1)));
    }
}

public sealed class ModelProfileDefaultsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Unconfigured_capabilities_read_as_none()
    {
        var profile = ModelProfile.Register(
            ProviderKind.OpenCodeGo, "Ox Alpha Free", "id", EndpointKind.ChatCompletions, Now);

        Assert.False(profile.GetCapabilities().Streaming);
        Assert.Same(ProfileCapabilities.None.GetType(), profile.GetCapabilities().GetType());
    }

    [Fact]
    public void Null_capability_documents_are_rejected()
    {
        var profile = ModelProfile.Register(
            ProviderKind.OpenCodeGo, "Ox Alpha Free", "id", EndpointKind.ChatCompletions, Now);

#pragma warning disable CA1862 // Test intentionally passes a null literal.
        Assert.Throws<ArgumentNullException>(() => profile.SetCapabilities(null!, Now));
#pragma warning restore CA1862
    }
}

public sealed class WorkspaceCanonicalGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Validation_requires_a_canonical_root()
    {
        var workspace = Workspace.Register("Demo", @"C:\repos\demo", Now);

        Assert.Throws<ArgumentException>(
            () => workspace.MarkValidated("   ", Now));
    }
}

public sealed class DomainIdFormatTests
{
    [Theory]
    [InlineData("ws")]
    [InlineData("ses")]
    [InlineData("run")]
    [InlineData("todo")]
    [InlineData("apr")]
    [InlineData("tex")]
    [InlineData("chg")]
    [InlineData("flc")]
    [InlineData("model")]
    [InlineData("pol")]
    [InlineData("lse")]
    [InlineData("corr")]
    [InlineData("use")]
    public void Identifiers_use_the_expected_prefixes(string prefix)
    {
        var id = prefix switch
        {
            "ws" => DomainId.NewWorkspaceId(),
            "ses" => DomainId.NewSessionId(),
            "run" => DomainId.NewRunId(),
            "todo" => DomainId.NewTodoId(),
            "apr" => DomainId.NewApprovalId(),
            "tex" => DomainId.NewToolExecutionId(),
            "chg" => DomainId.NewChangeSetId(),
            "flc" => DomainId.NewFileChangeId(),
            "model" => DomainId.NewModelProfileId(),
            "pol" => DomainId.NewPolicyProfileId(),
            "lse" => DomainId.NewLeaseId(),
            "corr" => DomainId.NewCorrelationId(),
            _ => DomainId.NewUsageId(),
        };

        Assert.StartsWith($"{prefix}_", id, StringComparison.Ordinal);
        Assert.True(id.Length > prefix.Length + 8);
    }

    [Fact]
    public void Event_identifiers_embed_the_sequence()
    {
        var eventId = DomainId.NewEventId(42);

        Assert.StartsWith("evt_0000000042_", eventId, StringComparison.Ordinal);
    }
}


