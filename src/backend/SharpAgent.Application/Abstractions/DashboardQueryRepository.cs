using SharpAgent.Domain.Sessions;

namespace SharpAgent.Application.Abstractions;

/// <summary>
/// Persistence port for the bounded dashboard projection. Implementations return
/// domain-safe values only; EF entities never cross into the API layer.
/// </summary>
public interface IDashboardQueryRepository
{
    Task<DashboardQueryData> QueryAsync(
        int recentSessionLimit,
        bool includeArchived,
        DateTimeOffset? sinceUtc,
        CancellationToken cancellationToken);
}

public sealed record DashboardQueryData(
    IReadOnlyDictionary<SessionStatus, int> SessionsByState,
    int CompletedRuns,
    double? AverageDurationSeconds,
    int ApprovalCount,
    int ToolFailureCount,
    int ProviderFailureCount,
    int ContextCompactionCount,
    decimal? EstimatedCostUsd,
    IReadOnlyList<Session> RecentSessions);
