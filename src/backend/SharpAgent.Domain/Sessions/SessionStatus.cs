namespace SharpAgent.Domain.Sessions;

/// <summary>
/// Authoritative session lifecycle states (technical design section 5.1).
/// Archived visibility is tracked separately via <see cref="Sessions.Session.ArchivedAtUtc"/>.
/// </summary>
public enum SessionStatus
{
    Draft = 0,
    Planning = 1,
    Executing = 2,
    AwaitingApproval = 3,
    Reviewing = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
    Interrupted = 8,
}
