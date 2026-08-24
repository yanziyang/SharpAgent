using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Security;
using SharpAgent.Domain.Profiles;

namespace SharpAgent.Providers.Common;

/// <summary>
/// Shared bounded validation transport for OpenAI-compatible chat.completions
/// endpoints (DeepSeek, OpenRouter, and the OpenCode Go ChatCompletions style).
/// One short-lived, non-destructive stream check: bounded prompt, synthetic tool
/// schema, short timeout, no retry, bounded output, full redaction (plan 4.3).
/// </summary>
public sealed class ProviderValidationRunner
{
    public const int MaxStreamBytes = 512 * 1024;
    public const int MaxOutputTokens = 64;

    private static readonly JsonSerializerOptions RequestOptions = new(JsonSerializerDefaults.Web);

    private const string ValidationSystemPrompt =
        "You are being validated. Do not access files, networks, or tools other than the provided echo tool.";

    private const string ValidationUserPrompt =
        "Reply with exactly 'validation-ok' and then call the echo tool with {\"text\":\"ok\"}.";

    private static readonly ToolDefinition EchoTool = new(
        "echo_tool",
        "Echoes the provided text back unchanged. Safe, no side effects.",
        """{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}""");

    private readonly HttpClient _http;

    public ProviderValidationRunner(HttpClient http)
    {
        _http = http;
    }

    public async Task<ProfileValidationResult> ValidateAsync(
        string baseUrl,
        ModelProfile profile,
        ProviderSecretReference secretReference,
        CancellationToken cancellationToken)
    {
        if (profile.EndpointKind != EndpointKind.ChatCompletions)
        {
            return ProfileValidationResult.Failed(new NormalizedProviderError(
                ProviderErrorCategory.Unsupported,
                $"Endpoint style '{profile.EndpointKind}' is not supported yet."));
        }

        var key = Environment.GetEnvironmentVariable(secretReference.EnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(key))
        {
            return ProfileValidationResult.Failed(new NormalizedProviderError(
                ProviderErrorCategory.InvalidRequest,
                $"Provider secret '{secretReference.EnvironmentVariableName}' is not configured on this server."));
        }

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();

        var request = new ChatCompletionRequest(
            profile.ProviderModelId,
            [
                new ChatMessage("system", ValidationSystemPrompt),
                new ChatMessage("user", ValidationUserPrompt),
            ],
            Tools: [EchoTool],
            Stream: true,
            MaxTokens: MaxOutputTokens,
            Temperature: 0m);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint(baseUrl))
        {
            Content = new StringContent(JsonSerializer.Serialize(request, RequestOptions), Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TimeoutResult();
        }
        catch (HttpRequestException)
        {
            return ProfileValidationResult.Failed(new NormalizedProviderError(
                ProviderErrorCategory.Unavailable, "The provider could not be reached."));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var rawBody = await ReadBoundedBodyAsync(response, cancellationToken).ConfigureAwait(false);
                var error = ProviderErrorMapper.Map((int)response.StatusCode, rawBody);
                return ProfileValidationResult.Failed(error);
            }

            var dataFrames = await ReadStreamFramesAsync(
                response.Content,
                cancellationToken).ConfigureAwait(false);
            if (dataFrames.Error is not null)
            {
                return ProfileValidationResult.Failed(dataFrames.Error);
            }

            var normalized = ChatStreamNormalizer.Normalize(dataFrames.Frames);
            if (normalized.Error is not null)
            {
                return ProfileValidationResult.Failed(normalized.Error);
            }

            var latencyMs = System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            var streaming = normalized.Fragments.Any(static fragment =>
                            fragment.Kind is StreamFragmentKind.TextDelta or StreamFragmentKind.Finish);
            var toolCalling = normalized.Fragments.Any(static fragment =>
                fragment.Kind == StreamFragmentKind.ToolCall
                && !string.IsNullOrWhiteSpace(fragment.ToolCall?.Name));
            var usage = normalized.Fragments.LastOrDefault(static fragment => fragment.Kind == StreamFragmentKind.Usage)?.Usage
                        ?? NormalizedUsage.None;

            return new ProfileValidationResult(
                streaming,
                toolCalling,
                profile.GetCapabilities().ContextWindowTokens,
                (long)Math.Round(latencyMs),
                NormalizedProviderError.None);
        }
    }

    private static Uri BuildEndpoint(string baseUrl) =>
        new($"{baseUrl.TrimEnd('/')}/chat/completions");

    private static ProfileValidationResult TimeoutResult() =>
        ProfileValidationResult.Failed(new NormalizedProviderError(
            ProviderErrorCategory.Timeout, "The provider did not respond in time."));

    private static async Task<string?> ReadBoundedBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var buffer = new char[ProviderErrorMapper.MaxErrorMessageLength + 1];
        using var bodyStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var bodyReader = new StreamReader(bodyStream, Encoding.UTF8);
        var read = await bodyReader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
        var text = new string(buffer, 0, read);
        return SecretRedactor.Redact(text);
    }

    private static async Task<StreamReadResult> ReadStreamFramesAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var frames = new List<string>();
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        long bytesRead = 0;
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            bytesRead += Encoding.UTF8.GetByteCount(line) + 2;
            if (bytesRead > MaxStreamBytes)
            {
                return new StreamReadResult(frames, new NormalizedProviderError(
                    ProviderErrorCategory.Malformed, "Provider stream exceeded the bounded size."));
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                var payload = line[5..].Trim();
                if (payload == "[DONE]")
                {
                    break;
                }

                if (payload.Length > 0)
                {
                    frames.Add(payload);
                }
            }
        }

        return new StreamReadResult(frames, null);
    }

    private sealed record StreamReadResult(IReadOnlyList<string> Frames, NormalizedProviderError? Error);
}
