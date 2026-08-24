namespace SharpAgent.Domain.Workspaces;

public enum WorkspaceStatus
{
    PendingValidation = 0,
    Available = 1,
    Unavailable = 2,
    ValidationFailed = 3,
}

/// <summary>
/// Operator-managed workspace aggregate. <see cref="CanonicalRootPath"/> is captured at
/// validation time and re-canonicalized before every tool action (FR-002).
/// </summary>
public sealed class Workspace
{
    public string Id { get; init; } = DomainId.NewWorkspaceId();

    public string Name { get; internal set; } = string.Empty;

    /// <summary>The registered root as submitted by the operator.</summary>
    public string RootPath { get; internal set; } = string.Empty;

    /// <summary>Canonical resolved root captured during validation.</summary>
    public string? CanonicalRootPath { get; internal set; }

    public WorkspaceStatus Status { get; internal set; }

    /// <summary>JSON array of allowed relative path rules (non-secret metadata).</summary>
    public string AllowedPathsJson { get; internal set; } = "[]";

    public string? DefaultModelProfileId { get; internal set; }

    /// <summary>Safe availability detail for display; never contains raw OS errors.</summary>
    public string? ValidationMessage { get; internal set; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; internal set; }

    private Workspace()
    {
    }

    public static Workspace Register(string name, string rootPath, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Workspace name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Workspace root path is required.", nameof(rootPath));
        }

        return new Workspace
        {
            Name = name,
            RootPath = rootPath,
            Status = WorkspaceStatus.PendingValidation,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void MarkValidated(string canonicalRootPath, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(canonicalRootPath))
        {
            throw new ArgumentException("Canonical root path is required.", nameof(canonicalRootPath));
        }

        CanonicalRootPath = canonicalRootPath;
        Status = WorkspaceStatus.Available;
        ValidationMessage = null;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkUnavailable(string safeMessage, DateTimeOffset nowUtc) =>
        ApplyFailure(WorkspaceStatus.Unavailable, safeMessage, canonical: null, nowUtc);

    public void MarkValidationFailed(string safeMessage, DateTimeOffset nowUtc) =>
        ApplyFailure(WorkspaceStatus.ValidationFailed, safeMessage, canonical: null, nowUtc);

    private void ApplyFailure(WorkspaceStatus status, string safeMessage, string? canonical, DateTimeOffset nowUtc)
    {
        Status = status;
        CanonicalRootPath = canonical;
        ValidationMessage = string.IsNullOrWhiteSpace(safeMessage) ? null : safeMessage;
        UpdatedAtUtc = nowUtc;
    }
}
