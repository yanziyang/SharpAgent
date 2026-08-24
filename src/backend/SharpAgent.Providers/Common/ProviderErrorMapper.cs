using System.Text.Json;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Security;

namespace SharpAgent.Providers.Common;

/// <summary>
/// Maps HTTP status codes and provider error bodies to safe canonical errors.
/// Payloads are redacted and bounded; raw provider text never escapes (FR-054,
/// FR-055).
/// </summary>
public static class ProviderErrorMapper
{
    public const int MaxErrorMessageLength = 300;

    public static NormalizedProviderError Map(int statusCode, string? rawBody) => statusCode switch
    {
        >= 200 and < 300 => NormalizedProviderError.None,
        401 or 403 => new NormalizedProviderError(ProviderErrorCategory.Authentication, Safe("authentication failed")),
        429 => new NormalizedProviderError(ProviderErrorCategory.RateLimited, Safe("rate limit reached")),
        408 => new NormalizedProviderError(ProviderErrorCategory.Timeout, Safe("provider request timed out")),
        >= 500 => new NormalizedProviderError(ProviderErrorCategory.Unavailable, Safe("provider unavailable")),
        400 or 404 or 422 => new NormalizedProviderError(
            ProviderErrorCategory.InvalidRequest,
            Safe(ExtractMessage(rawBody, "The provider rejected the request."))),
        _ => new NormalizedProviderError(ProviderErrorCategory.Other, Safe("unexpected provider response")),
    };

    /// <summary>Best-effort safe message extraction from an OpenAI/OpenRouter error body.</summary>
    public static string ExtractMessage(string? rawBody, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                {
                    return Bound(message.GetString() ?? fallback);
                }
            }

            if (document.RootElement.TryGetProperty("message", out var rootMessage)
                && rootMessage.ValueKind == JsonValueKind.String)
            {
                return Bound(rootMessage.GetString() ?? fallback);
            }
        }
        catch (JsonException)
        {
        }

        return fallback;
    }

    private static string Safe(string message) => SecretRedactor.Redact(Bound(message)) ?? message;

    private static string Bound(string message)
    {
        var trimmed = message.Trim();
        return trimmed.Length <= MaxErrorMessageLength ? trimmed : trimmed[..MaxErrorMessageLength];
    }
}
