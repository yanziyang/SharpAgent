namespace SharpAgent.Domain.Sessions;

/// <summary>
/// Session-level lease held while a run is active. Prevents concurrent runs and lets a
/// restart sweep mark abandoned work as interrupted (technical design 4.2/5.2).
/// </summary>
public sealed class RunLease
{
    public string Id { get; init; } = DomainId.NewLeaseId();

    public string SessionId { get; init; } = string.Empty;

    public string RunId { get; init; } = string.Empty;

    public DateTimeOffset AcquiredAtUtc { get; init; }

    /// <summary>Null while the lease is live.</summary>
    public DateTimeOffset? ReleasedAtUtc { get; internal set; }

    private RunLease()
    {
    }

    public static RunLease Acquire(string sessionId, string runId, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("Session and run ids are required.");
        }

        return new RunLease { SessionId = sessionId, RunId = runId, AcquiredAtUtc = nowUtc };
    }

    public void Release(DateTimeOffset nowUtc)
    {
        if (ReleasedAtUtc is not null)
        {
            return;
        }

        ReleasedAtUtc = nowUtc;
    }
}
