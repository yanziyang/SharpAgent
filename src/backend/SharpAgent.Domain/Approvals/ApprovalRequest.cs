namespace SharpAgent.Domain.Approvals;

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Denied = 2,
    Cancelled = 3,
    Expired = 4,
}

public enum ApprovalDecision
{
    ApproveOnce = 0,
    Deny = 1,
    CancelRun = 2,
}

/// <summary>
/// Single-use approval bound to one run and one immutable action fingerprint (FR-045).
/// Preview fields are set at creation and never change; only the decision is recorded.
/// </summary>
public sealed class ApprovalRequest
{
    public string Id { get; init; } = DomainId.NewApprovalId();

    public string RunId { get; init; } = string.Empty;

    /// <summary>Immutable fingerprint over run, action, workspace, content, environment, policy version.</summary>
    public string ActionFingerprint { get; init; } = string.Empty;

    public string ActionType { get; init; } = string.Empty;

    /// <summary>Bounded, safe summary of the exact action (command or patch preview).</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>JSON array of affected workspace-relative paths.</summary>
    public string AffectedPathsJson { get; init; } = "[]";

    public string? Reason { get; init; }

    public ApprovalStatus Status { get; internal set; } = ApprovalStatus.Pending;

    public ApprovalDecision? Decision { get; internal set; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    public DateTimeOffset? ResolvedAtUtc { get; internal set; }

    private ApprovalRequest()
    {
    }

    public static ApprovalRequest Create(
        string runId,
        string actionFingerprint,
        string actionType,
        string summary,
        string affectedPathsJson,
        string? reason,
        DateTimeOffset nowUtc,
        DateTimeOffset expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(actionFingerprint))
        {
            throw new ArgumentException("Action fingerprint is required.", nameof(actionFingerprint));
        }

        if (expiresAtUtc <= nowUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Approval must expire in the future.");
        }

        return new ApprovalRequest
        {
            RunId = runId,
            ActionFingerprint = actionFingerprint,
            ActionType = actionType,
            Summary = summary,
            AffectedPathsJson = affectedPathsJson,
            Reason = reason,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
        };
    }

    public bool IsExpired(DateTimeOffset nowUtc) => Status == ApprovalStatus.Pending && nowUtc >= ExpiresAtUtc;

    /// <summary>Resolves the pending approval exactly once. Returns the mapped decision.</summary>
    public ApprovalStatus Resolve(ApprovalDecision decision, DateTimeOffset nowUtc)
    {
        if (Status != ApprovalStatus.Pending)
        {
            throw new InvalidStateTransitionException("approval", Status.ToString(), $"resolve:{decision}");
        }

        Decision = decision;
        ResolvedAtUtc = nowUtc;
        Status = decision == ApprovalDecision.ApproveOnce
            ? ApprovalStatus.Approved
            : decision == ApprovalDecision.Deny
                ? ApprovalStatus.Denied
                : ApprovalStatus.Cancelled;

        return Status;
    }

    public void Expire(DateTimeOffset nowUtc)
    {
        if (Status != ApprovalStatus.Pending)
        {
            return; // Expiry never overrides a recorded human decision.
        }

        Status = ApprovalStatus.Expired;
        ResolvedAtUtc = nowUtc;
    }
}
