using SharpAgent.Application.Sessions;

namespace SharpAgent.Application.Dashboard;

public sealed record DashboardStateCountDto(string State, int Count);

/// <summary>Server-authoritative dashboard projection with bounded metrics.</summary>
public sealed record DashboardDto(
    int PeriodDays,
    IReadOnlyList<DashboardStateCountDto> SessionsByState,
    int CompletedRuns,
    double? AverageDurationSeconds,
    int ApprovalCount,
    int ToolFailureCount,
    int ProviderFailureCount,
    int ContextCompactionCount,
    decimal? EstimatedCostUsd,
    IReadOnlyList<SessionSummaryDto> RecentSessions);
