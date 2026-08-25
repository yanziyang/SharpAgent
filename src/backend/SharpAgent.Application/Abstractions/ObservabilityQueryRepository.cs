using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Sessions;

namespace SharpAgent.Application.Abstractions;

public interface IObservabilityQueryRepository
{
    Task<ObservabilityQueryData> QueryAsync(
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken);
}

public sealed record ObservabilityQueryData(
    IReadOnlyDictionary<SessionStatus, int> SessionStateCounts,
    double? AverageRunDurationSeconds,
    double? AverageTimeToFirstStatusSeconds,
    IReadOnlyDictionary<ApprovalStatus, int> ApprovalOutcomeCounts,
    int ToolFailureCount,
    int ProviderFailureCount,
    int ProviderFallbackCount,
    int InterruptedRunCount,
    int ResumedRunCount,
    int ContextCompactionCount,
    IReadOnlyList<ProviderUsageMetric> ProviderUsage,
    int PolicyDenialCount,
    int WorkspaceDenialCount);

public sealed record ProviderUsageMetric(
    string Provider,
    string ModelProfileId,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd,
    int RunCount);
