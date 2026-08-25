using SharpAgent.Domain.Common;

namespace SharpAgent.Application.Abstractions;

/// <summary>
/// Request-scoped correlation state. Background run work uses the durable
/// correlation id on the run rather than inheriting an HTTP request lifetime.
/// </summary>
public interface ICorrelationContext
{
    string CurrentId { get; }

    void SetCurrent(string correlationId);
}

public sealed class CorrelationContext : ICorrelationContext
{
    private string? _currentId;

    public string CurrentId => _currentId ??= DomainId.NewCorrelationId();

    public void SetCurrent(string correlationId)
    {
        if (!CorrelationIds.IsSafe(correlationId))
        {
            throw new ArgumentException("Correlation id contains unsupported characters.", nameof(correlationId));
        }

        _currentId = correlationId;
    }
}

/// <summary>Bounded transport conventions for correlation ids.</summary>
public static class CorrelationIds
{
    public const string HeaderName = "X-Correlation-ID";
    public const int MaxLength = 64;

    public static string Normalize(string? candidate) =>
        IsSafe(candidate) ? candidate! : DomainId.NewCorrelationId();

    public static bool IsSafe(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxLength)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}
