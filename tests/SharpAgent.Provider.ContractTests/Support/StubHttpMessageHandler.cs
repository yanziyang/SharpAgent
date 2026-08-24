using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace SharpAgent.Provider.ContractTests.Support;

/// <summary>
/// In-process fake provider server: returns recorded SSE/text responses and
/// records every outbound request so tests can assert translation and gating.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        _responder = responder;
    }

    public sealed record RecordedRequest(HttpRequestMessage Request, string? Body);

    public List<RecordedRequest> Requests { get; } = [];

    public static StubHttpMessageHandler Sse(string sseBody) =>
        new((_, _) => Task.FromResult(Ok("text/event-stream", sseBody)));

    public static StubHttpMessageHandler JsonError(HttpStatusCode status, string jsonBody) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        }));

    public static StubHttpMessageHandler Delay(TimeSpan delay) =>
        new(async (_, cancellationToken) =>
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return Ok("text/event-stream", "data: [DONE]\n\n");
        });

    private static HttpResponseMessage Ok(string contentType, string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, contentType),
    };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Requests.Add(new RecordedRequest(request, body));
        return await _responder(request, cancellationToken).ConfigureAwait(false);
    }

    public static string ReadRequestBody(RecordedRequest recorded) => recorded.Body ?? string.Empty;

    public static string? BearerToken(RecordedRequest recorded) =>
        recorded.Request.Headers.Authorization?.Scheme == "Bearer"
            ? recorded.Request.Headers.Authorization.Parameter
            : null;

    public static HttpRequestHeaders Headers(RecordedRequest recorded) => recorded.Request.Headers;
}
