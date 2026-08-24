using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SharpAgent.Api.IntegrationTests.TestSupport;
using Xunit;

namespace SharpAgent.Api.IntegrationTests.Sessions;

/// <summary>
/// Phase 1 exit criteria over HTTP: create/reload a draft session from fresh SQLite,
/// deterministic state transitions, resume with a new run id, and safe payloads.
/// </summary>
public sealed class SessionsApiTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApiTestHost _host = ApiTestHost.Start();

    [Fact]
    public async Task Draft_session_is_created_and_reloaded_from_fresh_sqlite()
    {
        var ids = await _host.SeedCatalogAsync();

        using var created = await PostAsync(
            "/api/sessions",
            new
            {
                workspaceId = ids.WorkspaceId,
                task = "Investigate the failing pricing test and propose a plan.",
                mode = "plan",
                modelProfileId = ids.ModelProfileId,
                policyProfileId = ids.PolicyProfileId,
            },
            "create-1");
        var createdBody = await created.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);

        using (var document = JsonDocument.Parse(createdBody))
        {
            Assert.Equal("draft", document.RootElement.GetProperty("status").GetString());
            Assert.Equal("plan", document.RootElement.GetProperty("mode").GetString());
        }

        var sessionId = JsonDocument.Parse(createdBody).RootElement.GetProperty("id").GetString()!;

        // Reload through a second GET: the exit-criterion round trip.
        using var reloadedResponse = await _host.Client.GetAsync($"/api/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, reloadedResponse.StatusCode);
        var reloadBody = await reloadedResponse.Content.ReadAsStringAsync();
        Assert.Contains(sessionId, reloadBody, StringComparison.Ordinal);
        Assert.Contains("\"runs\":[]", reloadBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_idempotency_key_returns_a_stable_problem()
    {
        var ids = await _host.SeedCatalogAsync();
        var body = new
        {
            workspaceId = ids.WorkspaceId,
            task = "t",
            mode = "plan",
            modelProfileId = ids.ModelProfileId,
            policyProfileId = ids.PolicyProfileId,
        };

        using var response = await _host.Client.PostAsync(
            "/api/sessions", new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemContract>(JsonOptions);
        Assert.Equal("idempotency_key_required", problem!.Code);
    }

    [Fact]
    public async Task Replaying_the_same_idempotency_key_does_not_create_twice()
    {
        var ids = await _host.SeedCatalogAsync();

        var payload = new
        {
            workspaceId = ids.WorkspaceId,
            task = "Same payload both times",
            mode = "plan",
            modelProfileId = ids.ModelProfileId,
            policyProfileId = ids.PolicyProfileId,
        };

        using var first = await PostAsync("/api/sessions", payload, "replay-key");
        using var second = await PostAsync("/api/sessions", payload, "replay-key");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode); // replay returns the stored result

        using var list = await _host.Client.GetAsync("/api/sessions");
        using var listDocument = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(1, listDocument.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Same_key_with_a_different_payload_conflicts()
    {
        var ids = await _host.SeedCatalogAsync();
        var payload = new
        {
            workspaceId = ids.WorkspaceId,
            task = "First",
            mode = "plan",
            modelProfileId = ids.ModelProfileId,
            policyProfileId = ids.PolicyProfileId,
        };

        using var original = await PostAsync("/api/sessions", payload, "dup");
        Assert.Equal(HttpStatusCode.Created, original.StatusCode);

        using var conflictResponse = await PostAsync(
            "/api/sessions", payload with { task = "Second" }, "dup");

        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        var problem = await conflictResponse.Content.ReadFromJsonAsync<ProblemContract>(JsonOptions);
        Assert.Equal("idempotency_conflict", problem!.Code);
    }

    [Fact]
    public async Task Unknown_workspace_yields_field_level_validation_problem()
    {
        var ids = await _host.SeedCatalogAsync();

        using var response = await PostAsync(
            "/api/sessions",
            new
            {
                workspaceId = "ws_missing",
                task = "task text",
                mode = "plan",
                modelProfileId = ids.ModelProfileId,
                policyProfileId = ids.PolicyProfileId,
            },
            "k-validation");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemContract>(JsonOptions);
        Assert.Equal("validation_error", problem!.Code);
        Assert.True(problem.Errors!.ContainsKey("workspaceId"));
    }

    [Fact]
    public async Task Start_cancel_then_resume_keeps_history_and_orders_events()
    {
        var ids0 = await _host.SeedCatalogAsync();
        var sessionId = await CreateSessionAsync(ids0.ModelProfileId, ids0.PolicyProfileId);

        // Start run one.
        using var startOne = await PostAsync(
            $"/api/sessions/{sessionId}/runs", new { instruction = "first pass" }, "run-1");
        Assert.Equal(HttpStatusCode.Accepted, startOne.StatusCode);
        var runOne = await startOne.Content.ReadFromJsonAsync<StartRunResponse>(JsonOptions);
        Assert.Equal(1, runOne!.Run.Sequence);
        Assert.Equal("planning", runOne.Run.Status);

        // A concurrent start is rejected.
        using var startAgain = await PostAsync(
            $"/api/sessions/{sessionId}/runs", new { }, "run-1b");
        Assert.Equal(HttpStatusCode.Conflict, startAgain.StatusCode);

        // Cancel.
        using var cancel = await PostAsync(
            $"/api/sessions/{sessionId}/cancel", new { }, "cancel-1");
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        var cancelledSession = await cancel.Content.ReadFromJsonAsync<SessionResponse>(JsonOptions);
        Assert.Equal("cancelled", cancelledSession!.Status);

        // Resume creates run two; prior run stays in history untouched.
        using var startTwo = await PostAsync(
            $"/api/sessions/{sessionId}/runs",
            new { instruction = "continue", resumeFromRunId = runOne.Run.Id },
            "run-2");
        Assert.Equal(HttpStatusCode.Accepted, startTwo.StatusCode);
        var runTwo = await startTwo.Content.ReadFromJsonAsync<StartRunResponse>(JsonOptions);

        Assert.NotEqual(runOne.Run.Id, runTwo!.Run.Id);
        Assert.Equal(2, runTwo.Run.Sequence);
        Assert.Equal(runOne.Run.Id, runTwo.Run.ResumeSourceRunId);

        var sessionAfterResume = await _host.Client.GetFromJsonAsync<SessionResponse>(
            $"/api/sessions/{sessionId}", JsonOptions);
        Assert.Equal(2, sessionAfterResume!.Runs.Count);
        Assert.Equal("cancelled", sessionAfterResume.Runs[0].Status);

        // Ordered audit replay proves the event-first flow end to end.
        var events = await _host.Client.GetFromJsonAsync<List<EventResponse>>(
            $"/api/sessions/{sessionId}/events", JsonOptions);
        Assert.NotNull(events);
        Assert.Equal([1L, 2L, 3L, 4L], events.Select(static e => e.Sequence));
        Assert.Equal(
            ExpectedEventTypes,
            events.Select(static e => e.Type));
    }

    [Fact]
    public async Task Session_payloads_expose_contracted_fields_and_never_secrets()
    {
        const string marker = "sk-totallyarealsecretvalue123456"; // sharpagent:fixture-secret
        var (_, modelProfileId, policyProfileId) =
            await _host.SeedCatalogAsync(configReference: $"env:KEY {marker}");
        var sessionId = await CreateSessionAsync(modelProfileId, policyProfileId);

        foreach (var path in new[]
                 {
                     $"/api/sessions/{sessionId}",
                     $"/api/sessions/{sessionId}/events",
                     "/api/model-profiles",
                     "/api/policy-profiles",
                 })
        {
            var body = await _host.Client.GetStringAsync(path);
            Assert.DoesNotContain(marker, body, StringComparison.Ordinal);
            Assert.DoesNotContain("configReference", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("providerModelId", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Database_probe_reports_healthy_once_migrations_run()
    {
        var health = await _host.Client.GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/health");

        Assert.Equal("degraded", health!["overall"].GetString()); // providers/executor still pending
        var checks = health["checks"].EnumerateArray().ToList();
        var database = checks.Single(check => check.GetProperty("name").GetString() == "database");
        Assert.Equal("healthy", database.GetProperty("status").GetString());
    }

    private static readonly string[] ExpectedEventTypes =
        ["session_created", "run_started", "run_cancelled", "run_started"];

    public void Dispose() => _host.Dispose();

    private async Task<string> CreateSessionAsync(string modelProfileId, string policyProfileId)
    {
        var ids = await _host.SeedCatalogAsync();
        using var response = await PostAsync(
            "/api/sessions",
            new
            {
                workspaceId = ids.WorkspaceId,
                task = "Task body.",
                mode = "plan",
                modelProfileId,
                policyProfileId,
            },
            $"create-{Guid.NewGuid():N}");
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<SessionResponse>(JsonOptions);
        return dto!.Id;
    }

    private async Task<HttpResponseMessage> PostAsync<T>(string url, T value, string idempotencyKey)
        where T : notnull
    {
        var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await _host.Client.SendAsync(message);
    }
}

internal sealed record ProblemContract(string Title, int Status, string Code, Dictionary<string, string[]>? Errors);

internal sealed record RunResponse(
    string Id,
    int Sequence,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    string? StopReason,
    string? ResumeSourceRunId);

internal sealed record SessionResponse(
    string Id,
    string Status,
    string Mode,
    string? ActiveRunId,
    bool Archived,
    List<RunResponse> Runs);

internal sealed record StartRunResponse(SessionResponse Session, RunResponse Run);

internal sealed record EventResponse(long Sequence, string Type, DateTimeOffset OccurredAtUtc, string PayloadJson);


