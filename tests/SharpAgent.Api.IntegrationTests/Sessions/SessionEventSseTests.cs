using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpAgent.Api.IntegrationTests.TestSupport;
using SharpAgent.Domain.Auditing;
using SharpAgent.Infrastructure.Persistence;
using Xunit;

namespace SharpAgent.Api.IntegrationTests.Sessions;

/// <summary>Phase 5 replay and reconnect behavior over the public SSE contract.</summary>
public sealed class SessionEventSseTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApiTestHost _host = ApiTestHost.Start();

    [Fact]
    public async Task Event_stream_replays_and_resumes_after_last_event_id()
    {
        var ids = await _host.SeedCatalogAsync();
        var sessionId = await CreateSessionAsync(ids);

        using var initialCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var initial = await OpenStreamAsync(sessionId, null, initialCancellation.Token);
        using var initialReader = new StreamReader(await initial.Content.ReadAsStreamAsync(initialCancellation.Token), Encoding.UTF8);

        var created = await ReadEventAsync(initialReader, initialCancellation.Token);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Equal("1", created["id"]);
        Assert.Equal("session_created", created["event"]);
        Assert.Contains("sessionId", created["data"], StringComparison.Ordinal);

        initialCancellation.Cancel();
        initial.Dispose();

        using var started = await PostAsync(
            $"/api/sessions/{sessionId}/runs",
            new { instruction = "stream this" },
            "sse-run-1");
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);

        using var reconnectCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var reconnect = await OpenStreamAsync(sessionId, "1", reconnectCancellation.Token);
        using var reconnectReader = new StreamReader(
            await reconnect.Content.ReadAsStreamAsync(reconnectCancellation.Token),
            Encoding.UTF8);

        var runStarted = await ReadEventAsync(reconnectReader, reconnectCancellation.Token);
        Assert.Equal("2", runStarted["id"]);
        Assert.Equal("run_started", runStarted["event"]);
        Assert.Contains("runId", runStarted["data"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_last_event_id_is_rejected_only_for_streaming_requests()
    {
        var ids = await _host.SeedCatalogAsync();
        var sessionId = await CreateSessionAsync(ids);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sessions/{sessionId}/events");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("Last-Event-ID", "not-a-sequence");

        using var response = await _host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid_last_event_id", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_event_types_are_rendered_as_status_with_original_type_in_data()
    {
        var ids = await _host.SeedCatalogAsync();
        var sessionId = await CreateSessionAsync(ids);
        await AppendEventAsync(sessionId, 2, "future_event");

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await OpenStreamAsync(sessionId, null, cancellation.Token);
        using var reader = new StreamReader(
            await response.Content.ReadAsStreamAsync(cancellation.Token),
            Encoding.UTF8);

        _ = await ReadEventAsync(reader, cancellation.Token);
        var unknown = await ReadEventAsync(reader, cancellation.Token);

        Assert.Equal("2", unknown["id"]);
        Assert.Equal("status", unknown["event"]);
        Assert.Contains("\"type\":\"future_event\"", unknown["data"], StringComparison.Ordinal);
        cancellation.Cancel();
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private async Task<string> CreateSessionAsync(
        (string WorkspaceId, string ModelProfileId, string PolicyProfileId) ids)
    {
        using var response = await PostAsync(
            "/api/sessions",
            new
            {
                workspaceId = ids.WorkspaceId,
                task = "SSE task",
                mode = "plan",
                modelProfileId = ids.ModelProfileId,
                policyProfileId = ids.PolicyProfileId,
            },
            $"sse-create-{Guid.NewGuid():N}");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("id").GetString()!;
    }

    private async Task AppendEventAsync(string sessionId, long sequence, string type)
    {
        await using var scope = _host.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<SharpAgentDbContext>();
        await context.AuditEvents.AddAsync(
            AuditEvent.Create(
                sessionId,
                runId: null,
                sequence,
                type,
                "{}",
                DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> OpenStreamAsync(
        string sessionId,
        string? lastEventId,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sessions/{sessionId}/events");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (lastEventId is not null)
        {
            request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
        }

        return await _host.Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> PostAsync<T>(string url, T value, string idempotencyKey)
        where T : notnull
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await _host.Client.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, string>> ReadEventAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (fields.Count > 0)
                {
                    return fields;
                }

                continue;
            }

            if (line[0] == ':')
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator > 0)
            {
                fields[line[..separator]] = line[(separator + 1)..].TrimStart();
            }
        }

        throw new EndOfStreamException("The SSE connection closed before an event was received.");
    }
}
