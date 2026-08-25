using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Runs;
using SharpAgent.Application.Sessions;
using SharpAgent.Application.Tools;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Todos;
using SharpAgent.Domain.Sessions;
using SharpAgent.TestKit.Fakes;
using SharpAgent.TestKit.Workspaces;
using SharpAgent.Application.Tests.Support;
using Xunit;

namespace SharpAgent.Application.Tests.Runs;

/// <summary>
/// RunOrchestrator contracts (plan 11.2): events persisted in order, terminal
/// transitions, retained state for resumes, and the runtime being swappable
/// without touching policy, persistence, API, or UI.
/// </summary>
public sealed class RunOrchestratorTests : IDisposable
{
    private readonly SessionServiceFixture _fixture = new();
    private readonly TempWorkspace _workspace = TempWorkspace.Create();
    private readonly RecordingWorkspaceFakes _workspaceFakes;
    private readonly FakeChatClient _chat = new();

    public RunOrchestratorTests()
    {
        _workspaceFakes = new RecordingWorkspaceFakes(_workspace);
    }

    public void Dispose()
    {
        _chat.Dispose();
        _workspaceFakes.Dispose();
        _workspace.Dispose();
    }

    private async Task<(string SessionId, string RunId)> NewStartedSessionAsync(SessionMode mode = SessionMode.Execute)
    {
        var created = await _fixture.Service.CreateAsync(
            new CreateSessionRequest(_fixture.WorkspaceId, "p4 task", mode, _fixture.ModelProfileId, _fixture.PolicyProfileId),
            $"create-{Guid.NewGuid():N}");
        var started = await _fixture.Service.StartOrResumeAsync(
            created.Id,
            new StartRunRequest(null, null),
            $"run-{Guid.NewGuid():N}");
        return (started.Session.Id, started.Run.Id);
    }

    [Fact]
    public async Task Completed_run_persists_ordered_events_and_transitions_the_session()
    {
        var (sessionId, _) = await NewStartedSessionAsync();
        var runtime = new FakeAgentRuntime(async (_, sink) =>
        {
            await sink.EmitAsync(Event(RunEventKind.TodoCreated, todoText: "Plan the change"), CancellationToken.None);
            await sink.EmitAsync(Event(RunEventKind.AssistantSummary, text: "Implemented the feature."), CancellationToken.None);
            return new RunOutcome(RunStopReason.Completed, "Feature done.", 3);
        });

        var outcome = await WithRuntime(runtime).RunAsync(sessionId, CancellationToken.None);

        Assert.Equal(RunStopReason.Completed, outcome.StopReason);
        var session = await _fixture.Sessions.FindAsync(sessionId, CancellationToken.None);
        Assert.Equal(SessionStatus.Completed, session!.Status);
        Assert.Null(session.ActiveRunId);

        var replay = await _fixture.Events.ReplayAsync(sessionId, CancellationToken.None);
        Assert.Contains(replay, static auditEvent => auditEvent.Type == AuditEventTypes.TodoCreated);
        Assert.Contains(replay, static auditEvent => auditEvent.Type == AuditEventTypes.AssistantSummary);
        Assert.Contains(replay, static auditEvent => auditEvent.Type == AuditEventTypes.RunCompleted);
        Assert.Equal(replay.Count, replay.Select(static auditEvent => auditEvent.Sequence).Distinct().Count());

        var todos = await _fixture.Todos.ListBySessionAsync(sessionId, CancellationToken.None);
        Assert.Single(todos);
        Assert.Equal("Plan the change", todos[0].Text);
    }

    [Fact]
    public async Task Awaiting_approval_interrupts_the_session_durably()
    {
        var (sessionId, _) = await NewStartedSessionAsync();
        var runtime = new FakeAgentRuntime(async (_, sink) =>
        {
            await sink.EmitAsync(Event(RunEventKind.ToolStarted, toolName: "apply_patch"), CancellationToken.None);
            return new RunOutcome(RunStopReason.AwaitingApproval, "Awaiting a single-use approval.", 1);
        });

        var outcome = await WithRuntime(runtime).RunAsync(sessionId, CancellationToken.None);

        Assert.Equal(RunStopReason.AwaitingApproval, outcome.StopReason);
        var session = await _fixture.Sessions.FindAsync(sessionId, CancellationToken.None);
        Assert.Equal(SessionStatus.Interrupted, session!.Status);
        Assert.Null(session.ActiveRunId);
    }

    [Fact]
    public async Task Limits_interrupt_the_session_with_a_status_event()
    {
        var (sessionId, _) = await NewStartedSessionAsync();
        var runtime = new FakeAgentRuntime(async (_, sink) =>
        {
            await sink.EmitAsync(Event(RunEventKind.Status, text: "The maximum tool-call limit was reached."), CancellationToken.None);
            return new RunOutcome(RunStopReason.LimitReached, "Tool limit.", 5);
        });

        var outcome = await WithRuntime(runtime).RunAsync(sessionId, CancellationToken.None);

        Assert.Equal(RunStopReason.LimitReached, outcome.StopReason);
        var session = await _fixture.Sessions.FindAsync(sessionId, CancellationToken.None);
        Assert.Equal(SessionStatus.Interrupted, session!.Status);
        var replay = await _fixture.Events.ReplayAsync(sessionId, CancellationToken.None);
        Assert.Contains(replay, static auditEvent => auditEvent.Type == AuditEventTypes.Status);
    }

    [Fact]
    public async Task Provider_errors_fail_the_session_safely()
    {
        var (sessionId, _) = await NewStartedSessionAsync();
        var runtime = new FakeAgentRuntime((_, _) =>
            Task.FromResult(new RunOutcome(RunStopReason.ProviderError, "The provider interrupted the run.", 0)));

        var outcome = await WithRuntime(runtime).RunAsync(sessionId, CancellationToken.None);

        Assert.Equal(RunStopReason.ProviderError, outcome.StopReason);
        var session = await _fixture.Sessions.FindAsync(sessionId, CancellationToken.None);
        Assert.Equal(SessionStatus.Failed, session!.Status);
    }

    [Fact]
    public async Task Run_context_carries_retained_todos_and_compacted_history_for_resume()
    {
        var (sessionId, _) = await NewStartedSessionAsync();
        var first = new FakeAgentRuntime(async (_, sink) =>
        {
            await sink.EmitAsync(Event(RunEventKind.TodoCreated, todoText: "Retained plan item"), CancellationToken.None);
            return new RunOutcome(RunStopReason.Completed, "First run done.", 2);
        });
        await WithRuntime(first).RunAsync(sessionId, CancellationToken.None);

        var resumed = await _fixture.Service.StartOrResumeAsync(
            sessionId,
            new StartRunRequest(null, null),
            $"resume-{Guid.NewGuid():N}");
        var runtime = new FakeAgentRuntime(async (_, sink) =>
        {
            await sink.EmitAsync(Event(RunEventKind.AssistantSummary, text: "Continuing."), CancellationToken.None);
            return new RunOutcome(RunStopReason.Completed, "Resumed run done.", 1);
        });

        await WithRuntime(runtime).RunAsync(resumed.Session.Id, CancellationToken.None);

        var context = Assert.Single(runtime.Contexts);
        Assert.Contains(context.RetainedTodos, static todo => todo == "Retained plan item");
        Assert.Contains(context.CompactedHistorySummary!, "First run done.", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_cancels_the_session_durably()
    {
        var (sessionId, _) = await NewStartedSessionAsync();
        var runtime = new FakeAgentRuntime((_, _) => throw new OperationCanceledException());

        var outcome = await WithRuntime(runtime).RunAsync(sessionId, CancellationToken.None);

        Assert.Equal(RunStopReason.Cancelled, outcome.StopReason);
        var session = await _fixture.Sessions.FindAsync(sessionId, CancellationToken.None);
        Assert.Equal(SessionStatus.Cancelled, session!.Status);
    }

    [Fact]
    public async Task Every_canonical_event_kind_persists_through_the_sink()
    {
        var (sessionId, _) = await NewStartedSessionAsync();
        var runtime = new FakeAgentRuntime(async (_, sink) =>
        {
            await sink.EmitAsync(Event(RunEventKind.TodoCreated, todoText: "First item"), CancellationToken.None);
            await sink.EmitAsync(Event(RunEventKind.TodoCreated, todoText: "Second item"), CancellationToken.None);
            await sink.EmitAsync(Event(RunEventKind.TodoUpdated, todoText: "Second item"), CancellationToken.None);
            await sink.EmitAsync(Event(RunEventKind.ToolStarted, toolName: "read_file", text: "src/a.cs"), CancellationToken.None);
            await sink.EmitAsync(Event(RunEventKind.ToolOutput, toolName: "read_file", text: "file contents"), CancellationToken.None);
            await sink.EmitAsync(Event(RunEventKind.ToolCompleted, toolName: "read_file"), CancellationToken.None);
            await sink.EmitAsync(Event(RunEventKind.ContextCompacted, text: "Compacted."), CancellationToken.None);
            await sink.EmitAsync(Event(RunEventKind.UsageUpdated, text: "tokens in: 10, out: 5"), CancellationToken.None);
            return new RunOutcome(RunStopReason.Completed, "Done.", 1);
        });

        var outcome = await WithRuntime(runtime).RunAsync(sessionId, CancellationToken.None);

        Assert.Equal(RunStopReason.Completed, outcome.StopReason);
        var replay = await _fixture.Events.ReplayAsync(sessionId, CancellationToken.None);
        Assert.Contains(replay, static auditEvent => auditEvent.Type == AuditEventTypes.TodoUpdated);
        Assert.Contains(replay, static auditEvent => auditEvent.Type == AuditEventTypes.ToolOutput);
        Assert.Contains(replay, static auditEvent => auditEvent.Type == AuditEventTypes.ToolCompleted);
        Assert.Contains(replay, static auditEvent => auditEvent.Type == AuditEventTypes.ContextCompacted);
        Assert.Contains(replay, static auditEvent => auditEvent.Type == AuditEventTypes.UsageUpdated);

        // Deduplicated by text: two created + one completed update for the same item.
        var todos = await _fixture.Todos.ListBySessionAsync(sessionId, CancellationToken.None);
        Assert.Equal(2, todos.Count);
        Assert.Equal(TodoStatus.Completed, todos.Single(static todo => todo.Text == "Second item").Status);
    }

    [Fact]
    public async Task Safe_summaries_are_bounded_and_redacted()
    {
        var longText = new string('x', 10_000) + " sk-abcdef1234567890secret";

        var bounded = RunOrchestrator.SafeSummary(longText);

        Assert.True(bounded.Length <= RunOrchestrator.MaxSummaryCharacters);
        Assert.DoesNotContain("sk-abcdef", bounded, StringComparison.Ordinal);
    }

    private static RunEvent Event(
        RunEventKind kind,
        string? text = null,
        string? todoText = null,
        string? toolName = null) =>
        new(kind, text, TodoId: null, todoText, toolName, Detail: null, DateTimeOffset.UtcNow);

    private RunOrchestrator WithRuntime(IAgentRuntime runtime) => new(
        _fixture.Sessions,
        _fixture.Workspaces,
        _fixture.Profiles,
        _fixture.Policies,
        new FakeProviderAdapterRegistry(new FakeProviderAdapter(chatClientFactory: () => _chat)),
        _fixture.Todos,
        _fixture.Events,
        _fixture.Approvals,
        _fixture.Leases,
        _fixture.Worktrees,
        _fixture.UnitOfWork,
        _fixture.Clock,
        runtime,
        new WorkspaceToolService(
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
            null!,
            null!,
            _workspaceFakes.ProcessRunner,
            _workspaceFakes.Worktrees,
            FocusedCommandCatalog.Default));
}
