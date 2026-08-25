using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SharpAgent.Api.IntegrationTests.TestSupport;
using Xunit;

namespace SharpAgent.Api.IntegrationTests.Observability;

public sealed class Phase7ObservabilityEndpointTests : IDisposable
{
    private readonly ApiTestHost _host = ApiTestHost.Start();

    [Fact]
    public async Task Request_correlation_is_returned_and_carried_by_problem_details()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sessions/missing");
        request.Headers.Add("X-Correlation-ID", "phase7-correlation");

        using var response = await _host.Client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("phase7-correlation", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("phase7-correlation", body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Security_headers_are_present_on_api_responses()
    {
        using var response = await _host.Client.GetAsync("/api/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal(
            "default-src 'self'; connect-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Session_event_projection_contains_the_request_correlation()
    {
        var ids = await _host.SeedCatalogAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sessions")
        {
            Content = JsonContent.Create(new
            {
                workspaceId = ids.WorkspaceId,
                task = "Observe this request safely.",
                mode = "plan",
                modelProfileId = ids.ModelProfileId,
                policyProfileId = ids.PolicyProfileId,
            }),
        };
        request.Headers.Add("Idempotency-Key", "phase7-create");
        request.Headers.Add("X-Correlation-ID", "phase7-event-correlation");

        using var response = await _host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sessionId = created.RootElement.GetProperty("id").GetString();

        using var eventsResponse = await _host.Client.GetAsync($"/api/sessions/{sessionId}/events");
        using var events = JsonDocument.Parse(await eventsResponse.Content.ReadAsStringAsync());

        var firstEvent = events.RootElement.EnumerateArray().Single();
        Assert.Equal("phase7-event-correlation", firstEvent.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Metrics_endpoint_returns_bounded_aggregate_projection()
    {
        var ids = await _host.SeedCatalogAsync();
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/sessions")
        {
            Content = JsonContent.Create(new
            {
                workspaceId = ids.WorkspaceId,
                task = "Seed an aggregate metric.",
                mode = "plan",
                modelProfileId = ids.ModelProfileId,
                policyProfileId = ids.PolicyProfileId,
            }),
        };
        createRequest.Headers.Add("Idempotency-Key", "phase7-metric-create");
        using var createResponse = await _host.Client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var response = await _host.Client.GetAsync("/api/metrics?periodDays=999");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(30, document.RootElement.GetProperty("periodDays").GetInt32());
        Assert.True(document.RootElement.GetProperty("sessionStates").ValueKind == JsonValueKind.Array);
        Assert.Contains(
            document.RootElement.GetProperty("sessionStates").EnumerateArray(),
            item => item.GetProperty("key").GetString() == "draft" && item.GetProperty("count").GetInt32() == 1);
        Assert.True(document.RootElement.GetProperty("providerUsage").ValueKind == JsonValueKind.Array);
        Assert.True(document.RootElement.GetProperty("policyDenialCount").GetInt32() >= 0);
        Assert.True(document.RootElement.GetProperty("workspaceDenialCount").GetInt32() >= 0);
    }

    public void Dispose() => _host.Dispose();
}
