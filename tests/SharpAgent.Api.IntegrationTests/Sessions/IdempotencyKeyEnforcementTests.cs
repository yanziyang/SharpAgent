using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SharpAgent.Api.IntegrationTests.TestSupport;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Api.IntegrationTests.Sessions;

/// <summary>Every mutating route enforces the Idempotency-Key contract.</summary>
public sealed class IdempotencyKeyEnforcementTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApiTestHost _host = ApiTestHost.Start();

    [Fact]
    public async Task Session_mutations_reject_missing_idempotency_keys()
    {
        var ids = await _host.SeedCatalogAsync();
        var sessionId = await CreateSessionAsync(ids);

        foreach (var url in new[]
                 {
                     "/api/sessions",
                     $"/api/sessions/{sessionId}/runs",
                     $"/api/sessions/{sessionId}/cancel",
                     $"/api/sessions/{sessionId}/archive",
                     $"/api/sessions/{sessionId}/restore",
                 })
        {
            using var response = await _host.Client.PostAsync(
                url,
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemContract>(JsonOptions);
            Assert.Equal("idempotency_key_required", problem!.Code);
        }
    }

    [Fact]
    public async Task Workspace_registration_rejects_a_missing_idempotency_key()
    {
        using TempWorkspace directory = TempWorkspace.Create();

        using var response = await _host.Client.PostAsync(
            "/api/workspaces",
            new StringContent(
                JsonSerializer.Serialize(new { name = "Demo", rootPath = directory.RootPath }, JsonOptions),
                System.Text.Encoding.UTF8,
                "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemContract>(JsonOptions);
        Assert.Equal("idempotency_key_required", problem!.Code);
    }

    public void Dispose() => _host.Dispose();

    private async Task<string> CreateSessionAsync((string WorkspaceId, string ModelProfileId, string PolicyProfileId) ids)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/sessions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new
                    {
                        workspaceId = ids.WorkspaceId,
                        task = "task",
                        mode = "plan",
                        modelProfileId = ids.ModelProfileId,
                        policyProfileId = ids.PolicyProfileId,
                    },
                    JsonOptions),
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        message.Headers.Add("Idempotency-Key", $"create-{Guid.NewGuid():N}");

        using var response = await _host.Client.SendAsync(message);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return dto.GetProperty("id").GetString()!;
    }
}
