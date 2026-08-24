namespace SharpAgent.Domain.Tools;

public enum PolicyOutcome
{
    Allow = 0,
    RequireApproval = 1,
    Deny = 2,
}

public enum ToolExecutionStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2,
    Cancelled = 3,
}

/// <summary>
/// Record of one controlled tool execution. Only bounded previews are stored;
/// raw output and secrets never reach this row (FR-035, FR-055).
/// </summary>
public sealed class ToolExecution
{
    public string Id { get; init; } = DomainId.NewToolExecutionId();

    public string RunId { get; init; } = string.Empty;

    public string ToolName { get; init; } = string.Empty;

    /// <summary>Bounded, sanitized request summary.</summary>
    public string? RequestSummary { get; internal set; }

    public PolicyOutcome PolicyOutcome { get; init; }

    public string? ApprovalId { get; init; }

    public ToolExecutionStatus Status { get; internal set; } = ToolExecutionStatus.Running;

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? EndedAtUtc { get; internal set; }

    public int? ExitCode { get; internal set; }

    /// <summary>Truncated output preview; truncation is flagged explicitly (FR-035).</summary>
    public string? OutputPreview { get; internal set; }

    public bool OutputTruncated { get; internal set; }

    public bool RedactionApplied { get; internal set; }

    /// <summary>Safe error summary; never a raw exception or environment dump.</summary>
    public string? ErrorSummary { get; internal set; }

    private ToolExecution()
    {
    }

    public static ToolExecution Start(
        string runId,
        string toolName,
        PolicyOutcome outcome,
        string? approvalId,
        DateTimeOffset startedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("Tool name is required.", nameof(toolName));
        }

        return new ToolExecution
        {
            RunId = runId,
            ToolName = toolName,
            PolicyOutcome = outcome,
            ApprovalId = approvalId,
            StartedAtUtc = startedAtUtc,
        };
    }

    public void Complete(int? exitCode, string? outputPreview, bool outputTruncated, bool redactionApplied, DateTimeOffset nowUtc)
    {
        GuardActive();
        ExitCode = exitCode;
        OutputPreview = outputPreview;
        OutputTruncated = outputTruncated;
        RedactionApplied = redactionApplied;
        Status = ToolExecutionStatus.Completed;
        EndedAtUtc = nowUtc;
    }

    public void Fail(string safeErrorSummary, DateTimeOffset nowUtc)
    {
        GuardActive();
        ErrorSummary = safeErrorSummary;
        Status = ToolExecutionStatus.Failed;
        EndedAtUtc = nowUtc;
    }

    public void MarkCancelled(DateTimeOffset nowUtc)
    {
        GuardActive();
        Status = ToolExecutionStatus.Cancelled;
        EndedAtUtc = nowUtc;
    }

    private void GuardActive()
    {
        if (Status != ToolExecutionStatus.Running)
        {
            throw new InvalidStateTransitionException("tool execution", Status.ToString(), "final");
        }
    }
}
