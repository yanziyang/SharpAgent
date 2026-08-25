using System.Text.Json;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Sessions;

namespace SharpAgent.Application.Dashboard;

public sealed class DashboardQueryService(IDashboardQueryRepository repository, IClock clock)
{
    public const int DefaultPeriodDays = 30;
    private static readonly int[] SupportedPeriodDays = [7, 30, 90];
    private const int RecentSessionLimit = 20;

    public async Task<DashboardDto> GetAsync(int periodDays, CancellationToken cancellationToken)
    {
        var normalizedPeriodDays = SupportedPeriodDays.Contains(periodDays) ? periodDays : DefaultPeriodDays;
        var data = await repository
            .QueryAsync(
                RecentSessionLimit,
                includeArchived: false,
                clock.UtcNow.AddDays(-normalizedPeriodDays),
                cancellationToken)
            .ConfigureAwait(false);

        return new DashboardDto(
            normalizedPeriodDays,
            data.SessionsByState
                .OrderBy(static entry => entry.Key)
                .Select(static entry => new DashboardStateCountDto(
                    JsonNamingPolicy.CamelCase.ConvertName(entry.Key.ToString()),
                    entry.Value))
                .ToArray(),
            data.CompletedRuns,
            data.AverageDurationSeconds,
            data.ApprovalCount,
            data.ToolFailureCount,
            data.ProviderFailureCount,
            data.ContextCompactionCount,
            data.EstimatedCostUsd,
            data.RecentSessions.Select(SessionService.ProjectSummary).ToArray());
    }
}
