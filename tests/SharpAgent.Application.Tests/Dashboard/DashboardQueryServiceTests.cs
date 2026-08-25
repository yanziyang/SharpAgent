using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Dashboard;
using SharpAgent.Domain.Sessions;
using SharpAgent.TestKit.Fakes;
using Xunit;

namespace SharpAgent.Application.Tests.Dashboard;

public sealed class DashboardQueryServiceTests
{
    [Fact]
    public async Task Projects_bounded_metrics_and_safe_recent_session_summaries()
    {
        var now = new DateTimeOffset(2026, 8, 25, 9, 0, 0, TimeSpan.Zero);
        var session = Session.CreateNew("ws_1", "Inspect parser", SessionMode.Plan, "model_1", "policy_1", now);
        var repository = new StubDashboardRepository(new DashboardQueryData(
            new Dictionary<SessionStatus, int> { [SessionStatus.Completed] = 1 },
            CompletedRuns: 2,
            AverageDurationSeconds: 12.5,
            ApprovalCount: 3,
            ToolFailureCount: 1,
            ProviderFailureCount: 2,
            ContextCompactionCount: 4,
            EstimatedCostUsd: 0.42m,
            RecentSessions: [session]));

        var result = await new DashboardQueryService(repository, new FakeClock(now)).GetAsync(7, CancellationToken.None);

        Assert.Equal(7, result.PeriodDays);
        Assert.Equal("completed", Assert.Single(result.SessionsByState).State);
        Assert.Equal(2, result.CompletedRuns);
        Assert.Equal(12.5, result.AverageDurationSeconds);
        Assert.Equal(3, result.ApprovalCount);
        Assert.Equal(1, result.ToolFailureCount);
        Assert.Equal(2, result.ProviderFailureCount);
        Assert.Equal(4, result.ContextCompactionCount);
        Assert.Equal(0.42m, result.EstimatedCostUsd);
        Assert.Equal("Inspect parser", Assert.Single(result.RecentSessions).Task);
        Assert.Equal(20, repository.RequestedLimit);
        Assert.False(repository.IncludedArchived);
        Assert.Equal(now.AddDays(-7), repository.RequestedSinceUtc);
    }

    private sealed class StubDashboardRepository(DashboardQueryData data) : IDashboardQueryRepository
    {
        public int RequestedLimit { get; private set; }

        public bool IncludedArchived { get; private set; }

        public DateTimeOffset? RequestedSinceUtc { get; private set; }

        public Task<DashboardQueryData> QueryAsync(int recentSessionLimit, bool includeArchived, DateTimeOffset? sinceUtc, CancellationToken cancellationToken)
        {
            RequestedLimit = recentSessionLimit;
            IncludedArchived = includeArchived;
            RequestedSinceUtc = sinceUtc;
            return Task.FromResult(data);
        }
    }
}
