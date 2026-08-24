namespace SharpAgent.Domain.Idempotency;

/// <summary>
/// Stored result of one state-changing command, keyed by the caller-supplied
/// idempotency key. Retention is local and bounded (initially 24 hours,
/// technical design section 5.3).
/// </summary>
public sealed class IdempotencyRecord
{
    public string Key { get; init; } = string.Empty;

    /// <summary>Logical operation name (for example "create_session").</summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>SHA-256 over method, canonical path and request body.</summary>
    public string RequestHash { get; init; } = string.Empty;

    /// <summary>Serialized response returned on replay.</summary>
    public string ResponseJson { get; init; } = "{}";

    public int StatusCode { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset ExpiresAtUtc { get; init; }

    private IdempotencyRecord()
    {
    }

    public static IdempotencyRecord Create(
        string key,
        string operation,
        string requestHash,
        string responseJson,
        int statusCode,
        DateTimeOffset nowUtc,
        TimeSpan retention)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(requestHash))
        {
            throw new ArgumentException("Request hash is required.", nameof(requestHash));
        }

        if (retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), "Retention must be positive.");
        }

        return new IdempotencyRecord
        {
            Key = key,
            Operation = operation,
            RequestHash = requestHash,
            ResponseJson = responseJson,
            StatusCode = statusCode,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc + retention,
        };
    }

    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;
}
