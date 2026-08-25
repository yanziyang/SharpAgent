using System.Net.Http.Json;
using System.Text.Json;
using SharpAgent.Api.IntegrationTests.TestSupport;
using Xunit;

namespace SharpAgent.Api.IntegrationTests.Dashboard;

public sealed class DashboardEndpointTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ApiTestHost _host = ApiTestHost.Start();

    [Fact]
    public async Task Returns_the_bounded_server_dashboard_projection()
    {
        var response = await _host.Client.GetAsync("/api/dashboard");
        var payload = await response.Content.ReadFromJsonAsync<DashboardContract>(Json);

        Assert.True(response.IsSuccessStatusCode);
        Assert.NotNull(payload);
        Assert.NotNull(payload!.SessionsByState);
        Assert.NotNull(payload.RecentSessions);
        Assert.Equal(0, payload.CompletedRuns);
        Assert.Null(payload.AverageDurationSeconds);
        Assert.Null(payload.EstimatedCostUsd);
        Assert.Equal(0, payload.ApprovalCount);
        Assert.Equal(0, payload.ToolFailureCount);
        Assert.Equal(0, payload.ProviderFailureCount);
        Assert.Equal(0, payload.ContextCompactionCount);
        Assert.Equal(30, payload.PeriodDays);
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private sealed record DashboardContract(
        int PeriodDays,
        IReadOnlyList<StateCountContract> SessionsByState,
        int CompletedRuns,
        double? AverageDurationSeconds,
        int ApprovalCount,
        int ToolFailureCount,
        int ProviderFailureCount,
        int ContextCompactionCount,
        decimal? EstimatedCostUsd,
        IReadOnlyList<SessionContract> RecentSessions);

    private sealed record StateCountContract(string State, int Count);

    private sealed record SessionContract(string Id, string Task);
}
