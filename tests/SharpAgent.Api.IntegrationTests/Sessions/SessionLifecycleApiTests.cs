using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SharpAgent.Api.IntegrationTests.TestSupport;
using Xunit;

namespace SharpAgent.Api.IntegrationTests.Sessions;

/// <summary>Archive/restore, catalog, and not-found surfaces (Phase 1 API coverage).</summary>
public sealed class SessionLifecycleApiTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] SessionPropertyNames =
    [
        "activeRunId", "archived", "createdAtUtc", "id", "mode", "modelProfileId",
        "policyProfileId", "runs", "status", "task", "updatedAtUtc", "workspaceId",
    ];

    private static readonly string[] ProfilePropertyNames =
    [
        "contextWindowTokens", "displayName", "eligibleForExecute", "eligibleForPlan",
        "enabled", "estimatedUsdPerMillionInputTokens", "estimatedUsdPerMillionOutputTokens",
        "id", "provider", "streaming", "toolCalling", "validationStatus",
    ];

    private static readonly string[] PolicyPropertyNames =
    [
        "approvalExpiryMinutes", "id", "maxEstimatedCostUsd",
        "maxRunDurationMinutes", "maxToolCalls", "name",
    ];

    private readonly ApiTestHost _host = ApiTestHost.Start();

    [Fact]
    public async Task Archived_sessions_leave_the_default_list_and_return_on_restore()
    {
        var ids = await _host.SeedCatalogAsync();
        var sessionId = await CreatePlanSessionAsync(ids.WorkspaceId, ids.ModelProfileId, ids.PolicyProfileId);

        using var archive = await PostAsync($"/api/sessions/{sessionId}/archive", new { }, "archive-1");
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        var visible = JsonDocument.Parse(await _host.Client.GetStringAsync("/api/sessions"));
        Assert.Equal(0, visible.RootElement.GetArrayLength());

        var archivedList = JsonDocument.Parse(
            await _host.Client.GetStringAsync("/api/sessions?includeArchived=true"));
        Assert.Equal(1, archivedList.RootElement.GetArrayLength());

        using var restore = await PostAsync($"/api/sessions/{sessionId}/restore", new { }, "restore-1");
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var visibleAgain = JsonDocument.Parse(await _host.Client.GetStringAsync("/api/sessions"));
        Assert.Equal(1, visibleAgain.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Unknown_sessions_return_not_found_with_stable_code()
    {
        using var response = await _host.Client.GetAsync("/api/sessions/ses_missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemContract>(JsonOptions);
        Assert.Equal("not_found", problem!.Code);
    }

    [Fact]
    public async Task Catalogs_expose_only_contracted_safe_fields()
    {
        var ids = await _host.SeedCatalogAsync(configReference: "env:SHARPAGENT_OPENCODE_GO_API_KEY");

        var profilesBody = await _host.Client.GetStringAsync("/api/model-profiles");
        using (var document = JsonDocument.Parse(profilesBody))
        {
            var profile = document.RootElement[0];
            Assert.Equal(
                ProfilePropertyNames,
                profile.EnumerateObject().Select(static p => p.Name).Order());

            // The env-var NAME may appear as a reference; a raw value must never exist.
            Assert.DoesNotContain("sk-", profilesBody, StringComparison.Ordinal);
            Assert.DoesNotContain("providerModelId", profilesBody, StringComparison.OrdinalIgnoreCase);
        }

        var policiesBody = await _host.Client.GetStringAsync("/api/policy-profiles");
        using (var policies = JsonDocument.Parse(policiesBody))
        {
            var policy = policies.RootElement[0];
            Assert.Equal(
                PolicyPropertyNames,
                policy.EnumerateObject().Select(static p => p.Name).Order());
        }

        var workspacesBody = await _host.Client.GetStringAsync("/api/workspaces");
        using (var workspaces = JsonDocument.Parse(workspacesBody))
        {
            Assert.Equal(1, workspaces.RootElement.GetArrayLength());
            var workspace = workspaces.RootElement[0];
            Assert.Contains("rootPath", workspace.EnumerateObject().Select(static p => p.Name));
        }
    }

    [Fact]
    public async Task Session_projection_keeps_the_contracted_shape_over_http()
    {
        var ids = await _host.SeedCatalogAsync();
        var sessionId = await CreatePlanSessionAsync(ids.WorkspaceId, ids.ModelProfileId, ids.PolicyProfileId);

        var body = await _host.Client.GetStringAsync($"/api/sessions/{sessionId}");
        using var document = JsonDocument.Parse(body);

        Assert.Equal(
            SessionPropertyNames,
            document.RootElement.EnumerateObject().Select(static p => p.Name).Order());
    }

    public void Dispose() => _host.Dispose();

    private async Task<string> CreatePlanSessionAsync(string workspaceId, string modelProfileId, string policyProfileId)
    {
        using var response = await PostAsync(
            "/api/sessions",
            new
            {
                workspaceId,
                task = "Task for lifecycle tests.",
                mode = "plan",
                modelProfileId,
                policyProfileId,
            },
            $"create-{Guid.NewGuid():N}");
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return dto.GetProperty("id").GetString()!;
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

