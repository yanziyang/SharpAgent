using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Observability;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Sessions;
using SharpAgent.TestKit.Fakes;
using Xunit;

namespace SharpAgent.Application.Tests.Observability;

public sealed class ObservabilityQueryServiceTests
{
    [Fact]
    public async Task Normalizes_period_and_projects_all_safe_metric_groups()
    {
        var clock = FakeClock.At(2026, 8, 25, 12);
        var repository = new FakeRepository(new ObservabilityQueryData(
            new Dictionary<SessionStatus, int>
            {
                [SessionStatus.Completed] = 2,
                [SessionStatus.Interrupted] = 1,
            },
            12.5,
            0.8,
            new Dictionary<ApprovalStatus, int>
            {
                [ApprovalStatus.Approved] = 3,
                [ApprovalStatus.Expired] = 1,
            },
            2,
            1,
            4,
            1,
            2,
            3,
            [
                new ProviderUsageMetric("z-provider", "z-model", 10, 20, 0.30m, 1),
                new ProviderUsageMetric("a-provider", "a-model", 30, 40, 0.70m, 2),
            ],
            5,
            6));
        var service = new ObservabilityQueryService(repository, clock);

        var result = await service.GetAsync(999);

        Assert.Equal(30, result.PeriodDays);
        Assert.Equal(["completed", "interrupted"], result.SessionStates.Select(static item => item.Key));
        Assert.Equal([2, 1], result.SessionStates.Select(static item => item.Count));
        Assert.Equal(["approved", "expired"], result.ApprovalOutcomes.Select(static item => item.Key));
        Assert.Equal(12.5, result.AverageRunDurationSeconds);
        Assert.Equal(0.8, result.AverageTimeToFirstStatusSeconds);
        Assert.Equal(2, result.ToolFailureCount);
        Assert.Equal(1, result.ProviderFailureCount);
        Assert.Equal(4, result.ProviderFallbackCount);
        Assert.Equal(1, result.InterruptedRunCount);
        Assert.Equal(2, result.ResumedRunCount);
        Assert.Equal(3, result.ContextCompactionCount);
        Assert.Equal(["a-provider", "z-provider"], result.ProviderUsage.Select(static item => item.Provider));
        Assert.Equal(5, result.PolicyDenialCount);
        Assert.Equal(6, result.WorkspaceDenialCount);
        Assert.Equal(clock.UtcNow.AddDays(-30), repository.SinceUtc);
    }

    private sealed class FakeRepository(ObservabilityQueryData data) : IObservabilityQueryRepository
    {
        public DateTimeOffset SinceUtc { get; private set; }

        public Task<ObservabilityQueryData> QueryAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken)
        {
            SinceUtc = sinceUtc;
            return Task.FromResult(data);
        }
    }
}
