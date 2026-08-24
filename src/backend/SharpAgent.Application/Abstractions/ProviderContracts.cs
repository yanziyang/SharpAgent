using SharpAgent.Domain.Profiles;

namespace SharpAgent.Application.Abstractions;

/// <summary>Canonical request message exchanged between SharpAgent and any provider adapter.</summary>
public sealed record ChatMessage(string Role, string Content);

/// <summary>
/// Canonical provider request built by the adapters. Provider-specific request
/// shapes never cross the boundary in either direction (FR-051, FR-053).
/// </summary>
public sealed record ChatCompletionRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<ToolDefinition>? Tools = null,
    bool Stream = false,
    int? MaxTokens = null,
    decimal? Temperature = null);

/// <summary>Canonical, bounded tool definition used for synthetic validation schemas.</summary>
public sealed record ToolDefinition(
    string Name,
    string Description,
    string ParametersJson);

/// <summary>A single normalized tool invocation extracted from a provider stream.</summary>
public sealed record NormalizedToolCall(
    string Id,
    string Name,
    string ArgumentsJson);

/// <summary>Normalized token usage plus a cost estimate derived from profile prices.</summary>
public sealed record NormalizedUsage(
    int? InputTokens,
    int? OutputTokens,
    decimal? EstimatedUsd)
{
    public static NormalizedUsage None { get; } = new(null, null, null);
}

public enum StreamFragmentKind
{
    TextDelta = 0,
    ToolCall = 1,
    Finish = 2,
    Usage = 3,
}

/// <summary>One normalized fragment of a provider stream (FR-054).</summary>
public sealed record NormalizedStreamFragment(
    StreamFragmentKind Kind,
    string? Text,
    NormalizedToolCall? ToolCall,
    string? FinishReason,
    NormalizedUsage? Usage);

/// <summary>Safe, bounded classification of provider failures (never a raw payload).</summary>
public enum ProviderErrorCategory
{
    None = 0,
    Authentication = 1,
    RateLimited = 2,
    Unavailable = 3,
    Timeout = 4,
    Malformed = 5,
    InvalidRequest = 6,
    Unsupported = 7,
    Other = 8,
}

/// <summary>Normalized provider error: category plus redacted, bounded safe message.</summary>
public sealed record NormalizedProviderError(
    ProviderErrorCategory Category,
    string SafeMessage)
{
    public static NormalizedProviderError None { get; } = new(ProviderErrorCategory.None, string.Empty);
}

/// <summary>
/// Non-secret reference to where a provider credential lives (an environment
/// variable name). The value is resolved server-side only (FR-055).
/// </summary>
public sealed record ProviderSecretReference(string EnvironmentVariableName);

/// <summary>Safe capability and outcome metadata from a non-destructive validation run.</summary>
public sealed record ProfileValidationResult(
    bool Streaming,
    bool ToolCalling,
    int? ContextWindowTokens,
    long LatencyMs,
    NormalizedProviderError Error)
{
    public static ProfileValidationResult Failed(NormalizedProviderError error) =>
        new(false, false, null, 0, error);
}

/// <summary>
/// Provider-neutral adapter boundary. Implementations own provider-specific model
/// identifiers, endpoint styles, headers, retry policy, error payloads and secret
/// resolution; Application and API only ever see canonical contracts (FR-053,
/// FR-054). Chat-client creation for the MAF runtime arrives with Phase 4.
/// </summary>
public interface IModelProviderAdapter
{
    ProviderKind Provider { get; }

    /// <summary>
    /// Runs a bounded, non-destructive stream and tool-schema validation with no
    /// repository context. May touch the network, never the workspace.
    /// </summary>
    Task<ProfileValidationResult> ValidateAsync(
        ModelProfile profile,
        ProviderSecretReference secretReference,
        CancellationToken cancellationToken);
}

/// <summary>Resolves the adapter for a provider kind without leaking provider assemblies.</summary>
public interface IProviderAdapterRegistry
{
    IModelProviderAdapter? Find(ProviderKind provider);
}
