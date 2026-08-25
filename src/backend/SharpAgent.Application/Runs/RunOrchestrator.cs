using System.Text.Json;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Common;
using SharpAgent.Application.Providers;
using SharpAgent.Application.Security;
using SharpAgent.Application.Tools;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Todos;

namespace SharpAgent.Application.Runs;

/// <summary>
/// Drives one run through the agent runtime (plan section 11.1): resolves the
/// run context from persisted state, runs the runtime, persists every canonical
/// event BEFORE any follow-up, then applies the terminal session transition.
/// The runtime can be swapped for a fake without touching this flow.
/// </summary>
public sealed class RunOrchestrator(
    ISessionRepository sessions,
    IWorkspaceRepository workspaces,
    IModelProfileRepository profiles,
    IPolicyProfileRepository policies,
    IProviderAdapterRegistry adapters,
    ITodoRepository todos,
    IAuditEventRepository events,
    IApprovalRequestRepository approvals,
    IRunLeaseRepository leases,
    IGitWorktreeService worktrees,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAgentRuntime runtime,
    WorkspaceToolService toolService)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maximum safe summary length kept for resume; everything else is truncated.</summary>
    public const int MaxSummaryCharacters = 4_000;

    public async Task<RunOutcome> RunAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var session = await sessions.FindAsync(sessionId, cancellationToken).ConfigureAwait(false)
                      ?? throw new NotFoundException("session", sessionId);

        if (session.ActiveRunId is null)
        {
            throw new ConflictException("no_active_run", "Start a run before executing it.");
        }

        var run = session.Runs.Single(candidate => candidate.Id == session.ActiveRunId);
        var profile = await profiles.FindAsync(session.ModelProfileId, cancellationToken).ConfigureAwait(false)
                      ?? throw new NotFoundException("model profile", session.ModelProfileId);
        var policy = await policies.FindAsync(session.PolicyProfileId, cancellationToken).ConfigureAwait(false)
                     ?? throw new NotFoundException("policy profile", session.PolicyProfileId);
        var workspace = await workspaces.FindAsync(session.WorkspaceId, cancellationToken).ConfigureAwait(false)
                        ?? throw new NotFoundException("workspace", session.WorkspaceId);

        var adapter = adapters.Find(profile.Provider)
                      ?? throw new ConflictException(
                          "unsupported_provider",
                          $"No adapter is registered for provider '{profile.Provider}'.");
        var secretReference = new ProviderSecretReference(
            string.IsNullOrWhiteSpace(profile.ConfigReference)
                ? ProfileSecretDefaults.VariableFor(profile.Provider)
                : profile.ConfigReference);

        var chatClient = adapter.CreateChatClient(profile, secretReference);
        var bridge = new ToolProposalBridge(toolService);
        var retainedTodos = await LoadRetainedTodosAsync(session.Id, cancellationToken).ConfigureAwait(false);
        var historySummary = await BuildHistorySummaryAsync(session, run, cancellationToken).ConfigureAwait(false);

        var context = new RunContext(
session.Id,
            run.Id,
            workspace.Id,
            workspace.CanonicalRootPath ?? string.Empty,
            run.WorktreePath,
            session.Mode,
            session.Task,
            session.LastInstruction,
            chatClient,
            bridge,
            new RunLimits(
                MaxToolCalls: policy.MaxToolCalls,
                MaxDuration: TimeSpan.FromMinutes(policy.MaxRunDurationMinutes),
                MaxEstimatedCostUsd: policy.MaxEstimatedCostUsd,
                InputUsdPerMillionTokens: profile.GetCapabilities().EstimatedUsdPerMillionInputTokens,
                OutputUsdPerMillionTokens: profile.GetCapabilities().EstimatedUsdPerMillionOutputTokens),
            retainedTodos,
            historySummary,
            await LoadDecisionsSummaryAsync(session.Id, cancellationToken).ConfigureAwait(false));

        var sink = new PersistingEventSink(session, run.Id, events, todos, unitOfWork, clock);

        RunOutcome outcome;
        try
        {
            outcome = await runtime.RunAsync(context, sink, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Duration/limit interruptions are converted by the runtime itself; any
            // cancellation reaching the orchestrator is a user cancellation.
            outcome = new RunOutcome(RunStopReason.Cancelled, "The run was cancelled.", sink.ToolCallCount);
        }

        var now = clock.UtcNow;
        switch (outcome.StopReason)
        {
            case RunStopReason.Completed:
                session.CompleteActiveRun(SafeSummary(outcome.SafeMessage), now);
                await AppendEventAsync(session, run.Id, AuditEventTypes.RunCompleted, new { summary = SafeSummary(outcome.SafeMessage) }, cancellationToken).ConfigureAwait(false);
                break;

            case RunStopReason.AwaitingApproval:
            case RunStopReason.PolicyDenied:
            case RunStopReason.LimitReached:
                session.InterruptActiveRun(outcome.SafeMessage ?? outcome.StopReason.ToString(), now);
                await AppendEventAsync(session, run.Id, AuditEventTypes.Status, new { reason = outcome.StopReason.ToString(), message = SafeSummary(outcome.SafeMessage) }, cancellationToken).ConfigureAwait(false);
                break;

            case RunStopReason.ProviderError:
                session.FailActiveRun(outcome.SafeMessage ?? "Provider error.", now);
                await AppendEventAsync(session, run.Id, AuditEventTypes.RunFailed, new { reason = SafeSummary(outcome.SafeMessage) }, cancellationToken).ConfigureAwait(false);
                break;

            case RunStopReason.Cancelled:
                session.CancelActiveRun(outcome.SafeMessage ?? "Cancelled.", now);
                await AppendEventAsync(session, run.Id, AuditEventTypes.RunCancelled, new { reason = "cancelled" }, cancellationToken).ConfigureAwait(false);
                break;
        }

        await leases.ReleaseForRunAsync(run.Id, now, cancellationToken).ConfigureAwait(false);
        await RemoveWorktreeAsync(session, run.Id, cancellationToken).ConfigureAwait(false);
        await sink.PersistTodosAsync(cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return outcome;
    }

    private async Task<IReadOnlyList<string>> LoadRetainedTodosAsync(string sessionId, CancellationToken cancellationToken)
    {
        var list = await todos.ListBySessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return [.. list.OrderBy(static todo => todo.Sequence).Select(static todo => todo.Text)];
    }

    /// <summary>Safe compacted summary for resumes: latest run outcome, never raw transcripts.</summary>
    private async Task<string?> BuildHistorySummaryAsync(
        Domain.Sessions.Session session,
        AgentRun currentRun,
        CancellationToken cancellationToken)
    {
        var replay = await events.ReplayAsync(session.Id, cancellationToken).ConfigureAwait(false);
        var relevant = replay
                    .Where(auditEvent => auditEvent.RunId is not null && auditEvent.RunId != currentRun.Id)
                    .Where(static auditEvent => auditEvent.Type is AuditEventTypes.RunCompleted or AuditEventTypes.RunFailed or AuditEventTypes.RunCancelled)
                    .ToList();

        if (relevant.Count == 0)
        {
            return null;
        }

        var last = relevant[^1];
        string? payload = null;
        try
        {
            using var document = JsonDocument.Parse(last.PayloadJson);
            if (document.RootElement.TryGetProperty("summary", out var summary))
            {
                payload = summary.GetString();
            }
            else if (document.RootElement.TryGetProperty("reason", out var reason))
            {
                payload = reason.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return string.IsNullOrWhiteSpace(payload) ? null : SafeSummary(payload);
    }

    private async Task<IReadOnlyList<string>> LoadDecisionsSummaryAsync(string sessionId, CancellationToken cancellationToken)
    {
        var list = await approvals.ListPendingBySessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return [.. list.Take(5).Select(static approval => approval.Summary)];
    }

    private async Task AppendEventAsync(
        Domain.Sessions.Session session,
        string runId,
        string type,
        object payload,
        CancellationToken cancellationToken)
    {
        var sequence = session.ReserveNextEventSequence();
        var auditEvent = AuditEvent.Create(
            session.Id,
            runId,
            sequence,
            type,
            JsonSerializer.Serialize(payload, PayloadOptions),
            clock.UtcNow);

        await events.AddAsync(auditEvent, cancellationToken).ConfigureAwait(false);
    }

    private async Task RemoveWorktreeAsync(Domain.Sessions.Session session, string runId, CancellationToken cancellationToken)
    {
        var run = session.Runs.FirstOrDefault(candidate => candidate.Id == runId);
        if (run is null || string.IsNullOrEmpty(run.WorktreePath))
        {
            return;
        }

        try
        {
            await worktrees.RemoveAsync(
                    new WorktreeInfo(run.ExecutionEnvironmentId ?? "wt", run.WorktreePath),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static string SafeSummary(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Completed.";
        }

        var redacted = SecretRedactor.Redact(message) ?? string.Empty;
        return redacted.Length <= MaxSummaryCharacters ? redacted : redacted[..MaxSummaryCharacters];
    }

    /// <summary>
    /// Persists canonical run events as audit rows and tool records, in order,
    /// inside the surrounding unit of work (events durable BEFORE publication).
    /// Todo events also update the retained todo store for resume.
    /// </summary>
    private sealed class PersistingEventSink(
            Domain.Sessions.Session session,
            string runId,
            IAuditEventRepository events,
            ITodoRepository todos,
            IUnitOfWork unitOfWork,
            IClock clock) : IRunEventSink
    {
        public int ToolCallCount { get; private set; }

        private readonly List<TodoItem> _newTodos = [];
        private readonly Dictionary<string, TodoItem> _byText = [];
        private int _nextTodoSequence = 1;

        public async Task EmitAsync(RunEvent runEvent, CancellationToken cancellationToken)
        {
            var (type, payload) = Map(runEvent);
            var sequence = session.ReserveNextEventSequence();
            var auditEvent = AuditEvent.Create(
                session.Id,
                runId,
                sequence,
                type,
                JsonSerializer.Serialize(payload, PayloadOptions),
                runEvent.OccurredAtUtc);

            await events.AddAsync(auditEvent, cancellationToken).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Persists todos collected so far. Called once by the orchestrator at the end.</summary>
        public async Task PersistTodosAsync(CancellationToken cancellationToken)
        {
            if (_newTodos.Count > 0)
            {
                await todos.AddRangeAsync(_newTodos, cancellationToken).ConfigureAwait(false);
            }
        }

        private (string Type, object Payload) Map(RunEvent runEvent)
        {
            switch (runEvent.Kind)
            {
                case RunEventKind.AssistantSummary:
                    return (AuditEventTypes.AssistantSummary, new { summary = SafeSummary(runEvent.Text) });

                case RunEventKind.TodoCreated:
                    TrackTodo(runEvent);
                    return (AuditEventTypes.TodoCreated, new { todoId = runEvent.TodoId, text = SafeShort(runEvent.TodoText) });

                case RunEventKind.TodoUpdated:
                    TrackTodo(runEvent);
                    return (AuditEventTypes.TodoUpdated, new { todoId = runEvent.TodoId, text = SafeShort(runEvent.TodoText) });

                case RunEventKind.ToolStarted:
                    ToolCallCount++;
                    return (AuditEventTypes.ToolStarted, new { tool = SafeShort(runEvent.ToolName), detail = SafeShort(runEvent.Detail) });

                case RunEventKind.ToolOutput:
                    return (AuditEventTypes.ToolOutput, new { tool = SafeShort(runEvent.ToolName), output = SafeSummary(runEvent.Text) });

                case RunEventKind.ToolCompleted:
                    return (AuditEventTypes.ToolCompleted, new { tool = SafeShort(runEvent.ToolName), status = runEvent.Detail ?? "ok" });

                case RunEventKind.ContextCompacted:
                    return (AuditEventTypes.ContextCompacted, new { summary = SafeSummary(runEvent.Text) });

                case RunEventKind.Status:
                    return (AuditEventTypes.Status, new { message = SafeSummary(runEvent.Text) });

                case RunEventKind.UsageUpdated:
                    return (AuditEventTypes.UsageUpdated, new { detail = SafeShort(runEvent.Detail) });

                default:
                    return (AuditEventTypes.Status, new { message = "informational" });
            }
        }

        private void TrackTodo(RunEvent runEvent)
        {
            var text = SafeShort(runEvent.TodoText);
            if (text.Length == 0)
            {
                return;
            }

            if (_byText.TryGetValue(text, out var existing))
            {
                existing.TransitionTo(
                    runEvent.Kind == RunEventKind.TodoUpdated ? TodoStatus.Completed : TodoStatus.Pending,
                    clock.UtcNow);
                return;
            }

            var created = TodoItem.Create(session.Id, runId, _nextTodoSequence++, text, clock.UtcNow);
            if (runEvent.Kind == RunEventKind.TodoUpdated)
            {
                created.TransitionTo(TodoStatus.Completed, clock.UtcNow);
            }

            _newTodos.Add(created);
            _byText[text] = created;
        }

        private static string SafeShort(string? text) =>
            string.IsNullOrWhiteSpace(text) ? string.Empty : SafeSummary(text);
    }
}
