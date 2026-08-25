namespace SharpAgent.Domain.Sessions;

/// <summary>
/// Primary aggregate root. Owns lifecycle, the active-run reference, archive visibility,
/// optimistic version, and the last audit sequence watermark.
/// </summary>
public sealed class Session
{
    private readonly List<AgentRun> _runs = [];

    public string Id { get; init; } = DomainId.NewSessionId();

    public string WorkspaceId { get; init; } = string.Empty;

    /// <summary>Task text stored exactly as submitted (FR-011).</summary>
    public string Task { get; init; } = string.Empty;

    public SessionMode Mode { get; init; }

    public SessionStatus Status { get; private set; } = SessionStatus.Draft;

    public string ModelProfileId { get; init; } = string.Empty;

    public string PolicyProfileId { get; init; } = string.Empty;

    public string? ActiveRunId { get; internal set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    /// <summary>Follow-up instruction supplied with the most recent start/resume.</summary>
    public string? LastInstruction { get; private set; }

    /// <summary>Watermark of the last appended audit event sequence for this session.</summary>
    public long LastEventSequence { get; internal set; }

    /// <summary>Optimistic concurrency token (technical design section 4.2).</summary>
    public int Version { get; internal set; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; internal set; }

    public IReadOnlyList<AgentRun> Runs => _runs;

    /// <summary>
    /// Reserves the next audit-event sequence for this session. The unique
    /// (SessionId, Sequence) index is the persistence-level backstop.
    /// </summary>
    public long ReserveNextEventSequence()
    {
        LastEventSequence += 1;
        return LastEventSequence;
    }

    private Session()
    {
    }

    public static Session CreateNew(
        string workspaceId,
        string task,
        SessionMode mode,
        string modelProfileId,
        string policyProfileId,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new ArgumentException("Workspace id is required.", nameof(workspaceId));
        }

        if (string.IsNullOrWhiteSpace(task))
        {
            throw new ArgumentException("Task text is required.", nameof(task));
        }

        if (string.IsNullOrWhiteSpace(modelProfileId))
        {
            throw new ArgumentException("Model profile id is required.", nameof(modelProfileId));
        }

        if (string.IsNullOrWhiteSpace(policyProfileId))
        {
            throw new ArgumentException("Policy profile id is required.", nameof(policyProfileId));
        }

        return new Session
        {
            WorkspaceId = workspaceId,
            Task = task,
            Mode = mode,
            ModelProfileId = modelProfileId,
            PolicyProfileId = policyProfileId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    /// <summary>
    /// Starts or resumes work by creating a NEW run; prior runs stay immutable (FR-014).
    /// The caller must verify that no other active run exists before calling this
    /// (lease enforcement happens in the application/infrastructure layer).
    /// </summary>
    public AgentRun BeginRun(DateTimeOffset nowUtc, string? instruction = null, string? resumeSourceRunId = null)
    {
        if (ActiveRunId is not null)
        {
            throw new InvalidStateTransitionException(SessionStateMachine.EntityName, Status.ToString(), "new run");
        }

        var initialStatus = Mode == SessionMode.Plan ? RunStatus.Planning : RunStatus.Executing;
        var targetSessionStatus = initialStatus == RunStatus.Planning
            ? SessionStatus.Planning
            : SessionStatus.Executing;

        // Draft starts fresh; terminal states resume into a new run.
        SessionStateMachine.GuardTransition(Status, targetSessionStatus);

        if (!string.IsNullOrWhiteSpace(resumeSourceRunId)
            && _runs.All(run => run.Id != resumeSourceRunId))
        {
            throw new ArgumentException("Resume source run does not belong to this session.", nameof(resumeSourceRunId));
        }

        var sequence = _runs.Count == 0 ? 1 : _runs.Max(static run => run.Sequence) + 1;
        var run = AgentRun.StartNew(Id, sequence, initialStatus, nowUtc, resumeSourceRunId);
        _runs.Add(run);

        Status = targetSessionStatus;
        ActiveRunId = run.Id;
        LastInstruction = instruction;
        UpdatedAtUtc = nowUtc;

        return run;
    }

    /// <summary>Applies a lifecycle transition projected from the active run.</summary>
    public void ApplyTransition(SessionStatus target, DateTimeOffset nowUtc)
    {
        SessionStateMachine.GuardTransition(Status, target);
        Status = target;
        UpdatedAtUtc = nowUtc;

        if (SessionStateMachine.IsTerminal(target))
        {
            ActiveRunId = null;
        }
    }

    public void CancelActiveRun(string reason, DateTimeOffset nowUtc)
    {
        if (ActiveRunId is null)
        {
            throw new InvalidStateTransitionException(SessionStateMachine.EntityName, Status.ToString(), "cancelled (no active run)");
        }

        var run = _runs.Single(candidate => candidate.Id == ActiveRunId);
        run.Cancel(reason, nowUtc);

        ApplyTransition(SessionStatus.Cancelled, nowUtc);
    }

    public void CompleteActiveRun(string finalSummary, DateTimeOffset nowUtc)
    {
        if (ActiveRunId is null)
        {
            throw new InvalidStateTransitionException(SessionStateMachine.EntityName, Status.ToString(), "completed (no active run)");
        }

        var run = _runs.Single(candidate => candidate.Id == ActiveRunId);
        run.Complete(finalSummary, nowUtc);

        ApplyTransition(SessionStatus.Completed, nowUtc);
    }

    public void FailActiveRun(string reason, DateTimeOffset nowUtc)
    {
        if (ActiveRunId is null)
        {
            throw new InvalidStateTransitionException(SessionStateMachine.EntityName, Status.ToString(), "failed (no active run)");
        }

        var run = _runs.Single(candidate => candidate.Id == ActiveRunId);
        run.Fail(reason, nowUtc);

        ApplyTransition(SessionStatus.Failed, nowUtc);
    }

    public void InterruptActiveRun(string reason, DateTimeOffset nowUtc)
    {
        if (ActiveRunId is null)
        {
            throw new InvalidStateTransitionException(SessionStateMachine.EntityName, Status.ToString(), "interrupted (no active run)");
        }

        var run = _runs.Single(candidate => candidate.Id == ActiveRunId);
        run.Interrupt(reason, nowUtc);

        ApplyTransition(SessionStatus.Interrupted, nowUtc);
    }

    public void Archive(DateTimeOffset nowUtc)
    {
        if (ArchivedAtUtc is not null)
        {
            return; // Idempotent.
        }

        if (ActiveRunId is not null || SessionStateMachine.IsActive(Status))
        {
            throw new InvalidStateTransitionException(SessionStateMachine.EntityName, Status.ToString(), "archived (active run)");
        }

        ArchivedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void Restore(DateTimeOffset nowUtc)
    {
        if (ArchivedAtUtc is null)
        {
            return;
        }

        ArchivedAtUtc = null;
        UpdatedAtUtc = nowUtc;
    }
}
