namespace SharpAgent.Domain.Auditing;

/// <summary>Canonical user-visible event vocabulary (functional spec section 9.3).</summary>
public static class AuditEventTypes
{
    public const string SessionCreated = "session_created";
    public const string RunStarted = "run_started";
    public const string Status = "status";
    public const string AssistantSummary = "assistant_summary";
    public const string TodoCreated = "todo_created";
    public const string TodoUpdated = "todo_updated";
    public const string ContextCompacted = "context_compacted";
    public const string ToolProposed = "tool_proposed";
    public const string PolicyDecision = "policy_decision";
    public const string ApprovalRequested = "approval_requested";
    public const string ApprovalResolved = "approval_resolved";
    public const string ToolStarted = "tool_started";
    public const string ToolOutput = "tool_output";
    public const string ToolCompleted = "tool_completed";
    public const string WorkspaceDenied = "workspace_denied";
    public const string ChangeDetected = "change_detected";
    public const string ProviderFallback = "provider_fallback";
    public const string UsageUpdated = "usage_updated";
    public const string RunCompleted = "run_completed";
    public const string RunFailed = "run_failed";
    public const string RunCancelled = "run_cancelled";

    /// <summary>Unknown event types are persisted verbatim and rendered as information (FR-024 note).</summary>
    public static bool IsKnown(string type)
    {
        return type is SessionCreated or RunStarted or Status or AssistantSummary
            or TodoCreated or TodoUpdated or ContextCompacted or ToolProposed
            or PolicyDecision or ApprovalRequested or ApprovalResolved or ToolStarted
            or ToolOutput or ToolCompleted or WorkspaceDenied or ChangeDetected or ProviderFallback
            or UsageUpdated or RunCompleted or RunFailed or RunCancelled;
    }
}

/// <summary>
/// Append-only canonical event envelope. Sequence numbers are unique and monotonic
/// per session; rows are never updated or deleted.
/// </summary>
public sealed class AuditEvent
{
    public string Id { get; init; } = string.Empty;

    public string SessionId { get; init; } = string.Empty;

    public string? RunId { get; init; }

    /// <summary>Durable diagnostic correlation carried by every event.</summary>
    public string CorrelationId { get; init; } = DomainId.NewCorrelationId();

    /// <summary>One-based, gapless per session.</summary>
    public long Sequence { get; init; }

    public string Type { get; init; } = string.Empty;

    /// <summary>Server-safe JSON payload; never contains secrets or raw provider data.</summary>
    public string PayloadJson { get; init; } = "{}";

    public DateTimeOffset OccurredAtUtc { get; init; }

    private AuditEvent()
    {
    }

    public static AuditEvent Create(
        string sessionId,
        string? runId,
        long sequence,
        string type,
        string payloadJson,
        DateTimeOffset occurredAtUtc,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("Event type is required.", nameof(type));
        }

        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Sequence is one-based.");
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            payloadJson = "{}";
        }

        return new AuditEvent
        {
            Id = DomainId.NewEventId(sequence),
            SessionId = sessionId,
            RunId = runId,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? DomainId.NewCorrelationId()
                : correlationId,
            Sequence = sequence,
            Type = type,
            PayloadJson = payloadJson,
            OccurredAtUtc = occurredAtUtc,
        };
    }
}
