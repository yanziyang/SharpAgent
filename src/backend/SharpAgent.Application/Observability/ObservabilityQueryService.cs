using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Sessions;

namespace SharpAgent.Application.Observability;

public sealed class ObservabilityQueryService(IObservabilityQueryRepository repository, IClock clock)
{
    public const int DefaultPeriodDays = 30;

    private static readonly int[] SupportedPeriodDays = [7, 30, 90];

    public async Task<ObservabilityMetricsDto> GetAsync(
        int periodDays,
        CancellationToken cancellationToken = default)
    {
        var normalizedPeriodDays = SupportedPeriodDays.Contains(periodDays) ? periodDays : DefaultPeriodDays;
        var data = await repository
            .QueryAsync(clock.UtcNow.AddDays(-normalizedPeriodDays), cancellationToken)
            .ConfigureAwait(false);

        return new ObservabilityMetricsDto(
            normalizedPeriodDays,
            ToCounts(data.SessionStateCounts, SessionStateLabel),
            data.AverageRunDurationSeconds,
            data.AverageTimeToFirstStatusSeconds,
            ToCounts(data.ApprovalOutcomeCounts, ApprovalStatusLabel),
            data.ToolFailureCount,
            data.ProviderFailureCount,
            data.ProviderFallbackCount,
            data.InterruptedRunCount,
            data.ResumedRunCount,
            data.ContextCompactionCount,
            data.ProviderUsage
                .OrderBy(static usage => usage.Provider, StringComparer.Ordinal)
                .ThenBy(static usage => usage.ModelProfileId, StringComparer.Ordinal)
                .Select(static usage => new ProviderUsageMetricDto(
                    usage.Provider,
                    usage.ModelProfileId,
                    usage.InputTokens,
                    usage.OutputTokens,
                    usage.EstimatedCostUsd,
                    usage.RunCount))
                .ToArray(),
            data.PolicyDenialCount,
            data.WorkspaceDenialCount);
    }

    private static MetricCountDto[] ToCounts<TEnum>(
        IReadOnlyDictionary<TEnum, int> values,
        Func<TEnum, string> label)
        where TEnum : struct, Enum =>
        values
            .OrderBy(static item => item.Key.ToString(), StringComparer.Ordinal)
            .Select(item => new MetricCountDto(label(item.Key), item.Value))
            .ToArray();

    private static string SessionStateLabel(SessionStatus status) =>
        JsonName(status.ToString());

    private static string ApprovalStatusLabel(ApprovalStatus status) =>
        JsonName(status.ToString());

    private static string JsonName(string value) =>
        System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(value);
}
