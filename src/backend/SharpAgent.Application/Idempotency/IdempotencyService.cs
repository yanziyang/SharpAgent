using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Common;

namespace SharpAgent.Application.Idempotency;

public sealed record IdempotentResult<T>(T Value, bool Replayed);

/// <summary>
/// Implements the documented idempotency contract (technical design section 5.3):
/// same key + same request hash replays the stored result; same key with a different
/// hash is a 409 conflict; keys expire after the local retention period.
///
/// Fresh executions persist the response record INSIDE the command transaction so a
/// crash can never commit an operation without its idempotency receipt.
/// </summary>
public sealed class IdempotencyService(IIdempotencyStore store, IClock clock)
{
    /// <summary>Documented initial retention period.</summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromHours(24);

    public TimeSpan Retention { get; init; } = DefaultRetention;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>SHA-256 over the canonical JSON of the request payload (hex).</summary>
    public static string HashPayload<T>(T payload) where T : notnull
    {
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash);
    }

    public async Task<IdempotentResult<T>> ExecuteAsync<T>(
        IUnitOfWork unitOfWork,
        string key,
        string operation,
        string requestHash,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (await store.FindAsync(key, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            if (!existing.IsExpired(clock.UtcNow))
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)
                    || !string.Equals(existing.Operation, operation, StringComparison.Ordinal))
                {
                    throw new ConflictException(
                        "idempotency_conflict",
                        "This Idempotency-Key was already used with a different request.");
                }

                var cached = JsonSerializer.Deserialize<T>(existing.ResponseJson, SerializerOptions)
                             ?? throw new ConflictException(
                                 "idempotency_conflict",
                                 "Stored idempotency result could not be replayed.");

                return new IdempotentResult<T>(cached, Replayed: true);
            }

            // Expired keys may be reused; prune the stale record before re-executing.
            await store.DeleteExpiredAsync(clock.UtcNow, cancellationToken).ConfigureAwait(false);
        }

        var value = await unitOfWork.ExecuteInTransactionAsync(async transactionCancellationToken =>
        {
            var result = await action(transactionCancellationToken).ConfigureAwait(false);

            var record = Domain.Idempotency.IdempotencyRecord.Create(
                key,
                operation,
                requestHash,
                JsonSerializer.Serialize(result, SerializerOptions),
                statusCode: 201,
                clock.UtcNow,
                Retention);
            await store.AddAsync(record, transactionCancellationToken).ConfigureAwait(false);

            return result;
        }, cancellationToken).ConfigureAwait(false);

        return new IdempotentResult<T>(value, Replayed: false);
    }
}
