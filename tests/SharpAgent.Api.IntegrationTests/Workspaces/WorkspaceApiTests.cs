using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SharpAgent.Api.IntegrationTests.Sessions;
using SharpAgent.Api.IntegrationTests.TestSupport;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Api.IntegrationTests.Workspaces;

/// <summary>Workspace registration surface (FR-001 over HTTP).</summary>
public sealed class WorkspaceApiTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApiTestHost _host = ApiTestHost.Start();

    [Fact]
    public async Task Registration_creates_an_available_workspace()
    {
        using TempWorkspace directory = TempWorkspace.Create();

        using var response = await PostAsync(
            "/api/workspaces",
            new { name = "Local demo", rootPath = directory.RootPath },
            "ws-1");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Assert.Equal("Available", dto.GetProperty("status").GetString());
        Assert.Equal(directory.RootPath, dto.GetProperty("rootPath").GetString());
    }

    [Fact]
    public async Task Invalid_roots_are_rejected_with_field_errors()
    {
        using var response = await PostAsync(
            "/api/workspaces",
            new { name = "Ghost", rootPath = @"C:\definitely\not\here\sharpagent" },
            "ws-bad");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemContract>(JsonOptions);
        Assert.Equal("validation_error", problem!.Code);
        Assert.True(problem.Errors!.ContainsKey("rootPath"));
    }

    [Fact]
    public async Task Registration_replays_for_the_same_idempotency_key()
    {
        using TempWorkspace directory = TempWorkspace.Create();
        var payload = new { name = "Demo", rootPath = directory.RootPath };

        using var first = await PostAsync("/api/workspaces", payload, "same-ws-key");
        using var second = await PostAsync("/api/workspaces", payload, "same-ws-key");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetString();
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetString();
        Assert.Equal(firstId, secondId);

        using var list = await _host.Client.GetAsync("/api/workspaces");
        using var listDocument = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(1, listDocument.RootElement.GetArrayLength());
    }

    public void Dispose() => _host.Dispose();

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

