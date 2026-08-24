using System.Text.Json;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Common;
using SharpAgent.Application.Idempotency;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Todos;

namespace SharpAgent.Application.Sessions;

/// <summary>
/// Session lifecycle use cases. Every state change follows the event-first order:
/// mutate aggregates, append canonical audit events, commit once (design section 4.4).
/// </summary>
public sealed class SessionService(
    ISessionRepository sessions,
    IWorkspaceRepository workspaces,
    IModelProfileRepository modelProfiles,
    IPolicyProfileRepository policies,
    ITodoRepository todos,
    IAuditEventRepository events,
    IRunLeaseRepository leases,
    IIdempotencyStore idempotencyStore,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public const int MaxTaskLength = 8_000;

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private IdempotencyService Idempotency { get; } = new(idempotencyStore, clock);

    public Task<SessionDto> CreateAsync(
        CreateSessionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        WithIdempotency(
            idempotencyKey,
            OperationNames.CreateSession,
            request,
            async transactionCancellationToken =>
            {
                ValidateCreateRequest(request);

                var workspace = await workspaces.FindAsync(request.WorkspaceId, transactionCancellationToken)
                    .ConfigureAwait(false)
                    ?? throw ValidationException.ForField("workspaceId", "Unknown workspace.");

                var profile = await modelProfiles.FindAsync(request.ModelProfileId, transactionCancellationToken)
                    .ConfigureAwait(false)
                    ?? throw ValidationException.ForField("modelProfileId", "Unknown model profile.");

                _ = await policies.FindAsync(request.PolicyProfileId, transactionCancellationToken)
                    .ConfigureAwait(false)
                    ?? throw ValidationException.ForField("policyProfileId", "Unknown policy profile.");

                GuardModeEligibility(request.Mode, profile);

                var now = clock.UtcNow;
                var session = Domain.Sessions.Session.CreateNew(
                    workspace.Id, request.Task, request.Mode, profile.Id, request.PolicyProfileId, now);

                await sessions.AddAsync(session, transactionCancellationToken).ConfigureAwait(false);
                await AppendEventAsync(
                    session,
                    runId: null,
                    AuditEventTypes.SessionCreated,
                    new { workspaceId = workspace.Id, mode = request.Mode.ToString().ToLowerInvariant() },
                    transactionCancellationToken).ConfigureAwait(false);

                return session;
            },
            Project,
            cancellationToken);

    public Task<StartRunResult> StartOrResumeAsync(
        string sessionId,
        StartRunRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        WithIdempotency(
            idempotencyKey,
            OperationNames.StartRun,
            new { sessionId, request },
            async transactionCancellationToken =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
                ArgumentNullException.ThrowIfNull(request);

                var session = await RequireSessionAsync(sessionId, transactionCancellationToken).ConfigureAwait(false);

                if (session.ArchivedAtUtc is not null)
                {
                    throw new ConflictException("session_archived", "Restore the session before starting a run.");
                }

                if (session.ActiveRunId is not null
                    || await leases.FindActiveBySessionAsync(sessionId, transactionCancellationToken)
                        .ConfigureAwait(false) is not null)
                {
                    throw new ConflictException("session_active", "This session already has an active run.");
                }

                var profile = await modelProfiles.FindAsync(session.ModelProfileId, transactionCancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new NotFoundException("model profile", session.ModelProfileId);

                if (session.Mode == SessionMode.Execute && !profile.CanExecute())
                {
                    throw new ConflictException(
                        "profile_not_executable",
                        "This run needs a profile validated for streaming and tool calling.");
                }

                if (session.Mode == SessionMode.Plan && !profile.CanPlan())
                {
                    throw new ConflictException(
                        "profile_not_plannable",
                        "This run needs an enabled model profile.");
                }

                var run = session.BeginRun(
                    clock.UtcNow,
                    string.IsNullOrWhiteSpace(request.Instruction) ? null : request.Instruction.Trim(),
                    string.IsNullOrWhiteSpace(request.ResumeFromRunId) ? null : request.ResumeFromRunId.Trim());

                await leases.AddAsync(RunLease.Acquire(session.Id, run.Id, clock.UtcNow), transactionCancellationToken)
                    .ConfigureAwait(false);

                await AppendEventAsync(
                    session,
                    run.Id,
                    AuditEventTypes.RunStarted,
                    new
                    {
                        runId = run.Id,
                        sequence = run.Sequence,
                        mode = session.Mode.ToString().ToLowerInvariant(),
                        resumed = run.ResumeSourceRunId is not null,
                    },
                    transactionCancellationToken).ConfigureAwait(false);

                return session;
            },
            session => new StartRunResult(Project(session), ProjectRun(FindRun(session))),
            cancellationToken);

    public Task<SessionDto> CancelAsync(
        string sessionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        WithIdempotency(
            idempotencyKey,
            OperationNames.CancelRun,
            new { sessionId },
            async transactionCancellationToken =>
            {
                var session = await RequireSessionAsync(sessionId, transactionCancellationToken).ConfigureAwait(false);

                if (session.ActiveRunId is null)
                {
                    throw new ConflictException("no_active_run", "This session has no active run to cancel.");
                }

                var cancelledRunId = session.ActiveRunId;
                session.CancelActiveRun("Cancelled by developer.", clock.UtcNow);
                await leases.ReleaseForRunAsync(cancelledRunId!, clock.UtcNow, transactionCancellationToken)
                    .ConfigureAwait(false);

                await AppendEventAsync(
                    session,
                    cancelledRunId,
                    AuditEventTypes.RunCancelled,
                    new { reason = "cancelled_by_developer" },
                    transactionCancellationToken).ConfigureAwait(false);

                return session;
            },
            Project,
            cancellationToken);

    public Task<SessionDto> ArchiveAsync(
        string sessionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        WithIdempotency(
            idempotencyKey,
            OperationNames.ArchiveSession,
            new { sessionId, archive = true },
            async transactionCancellationToken =>
            {
                var session = await RequireSessionAsync(sessionId, transactionCancellationToken).ConfigureAwait(false);
                session.Archive(clock.UtcNow);
                return session;
            },
            Project,
            cancellationToken);

    public Task<SessionDto> RestoreAsync(
        string sessionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        WithIdempotency(
            idempotencyKey,
            OperationNames.RestoreSession,
            new { sessionId, restore = true },
            async transactionCancellationToken =>
            {
                var session = await RequireSessionAsync(sessionId, transactionCancellationToken).ConfigureAwait(false);
                session.Restore(clock.UtcNow);
                return session;
            },
            Project,
            cancellationToken);

    public async Task<SessionDto> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return Project(session);
    }

    public async Task<IReadOnlyList<SessionSummaryDto>> ListAsync(
        int page,
        int pageSize,
        bool includeArchived,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var recent = await sessions.ListRecentAsync(page, pageSize, includeArchived, cancellationToken)
            .ConfigureAwait(false);

        return [.. recent.Select(ProjectSummary)];
    }

    /// <summary>Ordered replay of the session audit history (SSE arrives in a later phase).</summary>
    public async Task<IReadOnlyList<AuditEventDto>> ReplayEventsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _ = await RequireSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var replay = await events.ReplayAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return [.. replay.Select(static auditEvent => new AuditEventDto(
            auditEvent.Sequence,
            auditEvent.Type,
            auditEvent.OccurredAtUtc,
            auditEvent.PayloadJson))];
    }

    /// <summary>Todos retained across resume; proves AC-05 context survival at store level.</summary>
    public async Task<IReadOnlyList<TodoItem>> ListTodosAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _ = await RequireSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return await todos.ListBySessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Shared idempotent-command wrapper: replays cached results for identical keys,
    /// rejects reused keys with different payloads, otherwise runs the mutation inside
    /// one transaction and stores the projected response before returning.
    /// </summary>
    private async Task<TProjection> WithIdempotency<TAggregate, TProjection>(
        string idempotencyKey,
        string operation,
        object requestPayload,
        Func<CancellationToken, Task<TAggregate>> mutate,
        Func<TAggregate, TProjection> project,
        CancellationToken cancellationToken) where TProjection : class
    {
        var requestHash = IdempotencyService.HashPayload(requestPayload);

        var result = await Idempotency.ExecuteAsync(
            unitOfWork,
            idempotencyKey,
            operation,
            requestHash,
            async transactionCancellationToken =>
            {
                var aggregate = await mutate(transactionCancellationToken).ConfigureAwait(false);
                return project(aggregate);
            },
            cancellationToken).ConfigureAwait(false);

        return result.Value;
    }

    private async Task<Domain.Sessions.Session> RequireSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        return await sessions.FindAsync(sessionId, cancellationToken).ConfigureAwait(false)
               ?? throw new NotFoundException("session", sessionId);
    }

    private static AgentRun FindRun(Domain.Sessions.Session session) =>
        session.Runs.OrderByDescending(static run => run.Sequence).First();

    private static void ValidateCreateRequest(CreateSessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            errors["workspaceId"] = ["Workspace is required."];
        }

        if (string.IsNullOrWhiteSpace(request.ModelProfileId))
        {
            errors["modelProfileId"] = ["Model profile is required."];
        }

        if (string.IsNullOrWhiteSpace(request.PolicyProfileId))
        {
            errors["policyProfileId"] = ["Policy profile is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Task))
        {
            errors["task"] = ["Task text is required."];
        }
        else if (request.Task.Length > MaxTaskLength)
        {
            errors["task"] = [$"Task text must be {MaxTaskLength} characters or fewer."];
        }

        if (!Enum.IsDefined(request.Mode))
        {
            errors["mode"] = ["Mode must be plan or execute."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    private static void GuardModeEligibility(SessionMode mode, Domain.Profiles.ModelProfile profile)
    {
        if (mode == SessionMode.Execute && !profile.CanExecute())
        {
            throw ValidationException.ForField(
                "modelProfileId",
                "Execute mode requires an enabled profile validated for streaming and tool calling.");
        }

        if (mode == SessionMode.Plan && !profile.CanPlan())
        {
            throw ValidationException.ForField(
                "modelProfileId",
                "Plan mode requires an enabled model profile.");
        }
    }

    private async Task AppendEventAsync(
        Domain.Sessions.Session session,
        string? runId,
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

    public static SessionDto Project(Domain.Sessions.Session session) => new(
        session.Id,
        session.WorkspaceId,
        session.Task,
        session.Mode,
        session.Status,
        session.ModelProfileId,
        session.PolicyProfileId,
        session.ActiveRunId,
        session.ArchivedAtUtc is not null,
        session.CreatedAtUtc,
        session.UpdatedAtUtc,
        [.. session.Runs.OrderBy(static run => run.Sequence).Select(ProjectRun)]);

    public static SessionSummaryDto ProjectSummary(Domain.Sessions.Session session) => new(
        session.Id,
        session.Task,
        session.Mode,
        session.Status,
        session.WorkspaceId,
        session.ModelProfileId,
        session.ActiveRunId,
        session.ArchivedAtUtc is not null,
        session.CreatedAtUtc,
        session.UpdatedAtUtc);

    public static RunDto ProjectRun(AgentRun run) => new(
        run.Id,
        run.Sequence,
        run.Status,
        run.StartedAtUtc,
        run.EndedAtUtc,
        run.StopReason,
        run.ResumeSourceRunId);
}

public sealed record StartRunResult(SessionDto Session, RunDto Run);



