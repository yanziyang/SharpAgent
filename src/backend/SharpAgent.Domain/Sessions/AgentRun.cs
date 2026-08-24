namespace SharpAgent.Domain.Sessions;

/// <summary>
/// One execution attempt within a session. Immutable once terminal; resume always
/// creates a new <see cref="AgentRun"/> (FR-014, AC-05).
/// </summary>
public sealed class AgentRun
{
    public string Id { get; init; } = DomainId.NewRunId();

    public string SessionId { get; init; } = string.Empty;

    /// <summary>One-based sequence within the session.</summary>
    public int Sequence { get; init; }

    public RunStatus Status { get; internal set; }

    public string? ResumeSourceRunId { get; init; }

    /// <summary>Correlation id propagated through logs and provider calls.</summary>
    public string CorrelationId { get; init; } = DomainId.NewCorrelationId();

    /// <summary>Worktree/container environment used by this run (Phase 2 records it).</summary>
    public string? ExecutionEnvironmentId { get; internal set; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; internal set; }

    public DateTimeOffset? CancelRequestedAtUtc { get; internal set; }

    /// <summary>Safe, bounded stop reason shown in the UI.</summary>
    public string? StopReason { get; internal set; }

    /// <summary>Compacted context summary retained across compaction/resume.</summary>
    public string? ContextSummary { get; internal set; }

    public string? FinalSummary { get; internal set; }

    private AgentRun()
    {
    }

    internal static AgentRun StartNew(
        string sessionId,
        int sequence,
        RunStatus initialStatus,
        DateTimeOffset startedAtUtc,
        string? resumeSourceRunId)
    {
        return new AgentRun
        {
            SessionId = sessionId,
            Sequence = sequence,
            Status = initialStatus,
            StartedAtUtc = startedAtUtc,
            ResumeSourceRunId = resumeSourceRunId,
        };
    }

    public void TransitionTo(RunStatus target, DateTimeOffset nowUtc)
    {
        RunStateMachine.GuardTransition(Status, target);

        if (RunStateMachine.IsTerminal(target))
        {
            EndedAtUtc = nowUtc;
        }

        Status = target;
    }

    public void Complete(string finalSummary, DateTimeOffset nowUtc)
    {
        TransitionTo(RunStatus.Reviewing, nowUtc);
        FinalSummary = finalSummary;
        TransitionTo(RunStatus.Completed, nowUtc);
    }

    public void Fail(string reason, DateTimeOffset nowUtc)
    {
        StopReason = reason;
        TransitionTo(RunStatus.Failed, nowUtc);
    }

    public void Cancel(string reason, DateTimeOffset nowUtc)
    {
        CancelRequestedAtUtc = nowUtc;
        StopReason = reason;
        TransitionTo(RunStatus.Cancelled, nowUtc);
    }

    public void Interrupt(string reason, DateTimeOffset nowUtc)
    {
        StopReason = reason;
        TransitionTo(RunStatus.Interrupted, nowUtc);
    }

    public void RecordCancellationRequest(DateTimeOffset nowUtc)
    {
        if (!RunStateMachine.IsActive(Status))
        {
            throw new InvalidStateTransitionException(RunStateMachine.EntityName, Status.ToString(), "cancellation request");
        }

        CancelRequestedAtUtc = nowUtc;
    }

    /// <summary>Records the worktree/container environment once execution starts (Phase 2).</summary>
    public void AssignEnvironment(string environmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);

        if (!RunStateMachine.IsActive(Status))
        {
            throw new InvalidStateTransitionException(RunStateMachine.EntityName, Status.ToString(), "assign environment");
        }

        ExecutionEnvironmentId = environmentId;
    }
}
