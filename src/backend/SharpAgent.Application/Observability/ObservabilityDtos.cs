namespace SharpAgent.Application.Observability;

public sealed record MetricCountDto(string Key, int Count);

public sealed record ProviderUsageMetricDto(
    string Provider,
    string ModelProfileId,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd,
    int RunCount);

/// <summary>
/// Bounded, server-authoritative operational metrics. It contains aggregate
/// facts only; paths, prompts, provider payloads and secrets never cross this DTO.
/// </summary>
public sealed record ObservabilityMetricsDto(
    int PeriodDays,
    IReadOnlyList<MetricCountDto> SessionStates,
    double? AverageRunDurationSeconds,
    double? AverageTimeToFirstStatusSeconds,
    IReadOnlyList<MetricCountDto> ApprovalOutcomes,
    int ToolFailureCount,
    int ProviderFailureCount,
    int ProviderFallbackCount,
    int InterruptedRunCount,
    int ResumedRunCount,
    int ContextCompactionCount,
    IReadOnlyList<ProviderUsageMetricDto> ProviderUsage,
    int PolicyDenialCount,
    int WorkspaceDenialCount);
