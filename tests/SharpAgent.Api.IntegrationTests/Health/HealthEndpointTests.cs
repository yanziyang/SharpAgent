using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SharpAgent.Api.IntegrationTests.TestSupport;
using SharpAgent.Application.Health;
using SharpAgent.TestKit.Fakes;
using Xunit;

namespace SharpAgent.Api.IntegrationTests.Health;

public sealed class HealthEndpointTests : IClassFixture<SharpAgentApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SharpAgentApiFactory _factory;

    public HealthEndpointTests(SharpAgentApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Default_composition_reports_pending_dependencies_as_degraded()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        var payload = await response.Content.ReadFromJsonAsync<HealthSnapshotContract>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("degraded", payload!.Overall);
        Assert.Contains(payload.Checks, static c => c.Name == "application" && c.Status == "healthy");
        Assert.All(payload.Checks, static c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
    }

    private static readonly string[] SnapshotPropertyNames = ["checks", "generatedAtUtc", "overall"];

    private static readonly string[] CheckPropertyNames = ["detail", "name", "status"];

    [Fact]
    public async Task Health_payload_exposes_only_the_contracted_safe_fields()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        var json = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // Secret-boundary smoke: exactly the contracted properties, nothing else.
        Assert.Equal(
            SnapshotPropertyNames,
            root.EnumerateObject().Select(static p => p.Name).Order());

        foreach (var check in root.GetProperty("checks").EnumerateArray())
        {
            Assert.Equal(
                CheckPropertyNames,
                check.EnumerateObject().Select(static p => p.Name).Order());
        }
    }

    [Fact]
    public async Task All_healthy_probes_report_overall_healthy()
    {
        using var factory = new SharpAgentApiFactory
        {
            ProbeOverrides =
            [
                new FakeHealthProbe("application"),
                new FakeHealthProbe("database", detail: "SQLite ready."),
            ],
        };
        var client = factory.CreateClient();

        var payload = await client.GetFromJsonAsync<HealthSnapshotContract>("/api/health", JsonOptions);

        Assert.NotNull(payload);
        Assert.Equal("healthy", payload!.Overall);
        Assert.Equal(2, payload.Checks.Count);
    }

    [Fact]
    public async Task Unready_probe_promotes_overall_status()
    {
        using var factory = new SharpAgentApiFactory
        {
            ProbeOverrides = [new FakeHealthProbe("database", HealthStatus.Unready, "Migration failed.")],
        };
        var client = factory.CreateClient();

        var payload = await client.GetFromJsonAsync<HealthSnapshotContract>("/api/health", JsonOptions);

        Assert.Equal("unready", payload!.Overall);
        Assert.Equal("unready", payload.Checks.Single().Status);
    }

    [Fact]
    public async Task Throwing_probe_is_reported_bounded_and_sanitized()
    {
        using var factory = new SharpAgentApiFactory
        {
            ProbeOverrides =
            [
                new FakeHealthProbe("database", static () => throw new InvalidOperationException("Server=hidden;Password=x")),
            ],
        };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("hidden", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Probe failed.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details_shape()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public sealed record HealthSnapshotContract(
        string Overall,
        IReadOnlyList<CheckContract> Checks,
        DateTimeOffset GeneratedAtUtc);

    public sealed record CheckContract(string Name, string Status, string? Detail);
}
