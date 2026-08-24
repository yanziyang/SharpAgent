using System.Net;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Profiles;
using SharpAgent.Provider.ContractTests.Support;
using SharpAgent.Providers;
using SharpAgent.Providers.Common;
using Xunit;

namespace SharpAgent.Provider.ContractTests;

/// <summary>
/// Full-adapter tests against a fake HTTP server: request translation, stream
/// normalization end to end, error mapping, timeout, cancellation and
/// pre-transport gating. Never touches a real provider (plan 10.2).
/// </summary>
public sealed class ProviderValidationRunnerTests : IDisposable
{
    private const string TestSecretVariable = "SHARPAGENT_TEST_PROVIDER_KEY";

    private readonly string _originalKey = Environment.GetEnvironmentVariable(TestSecretVariable) ?? string.Empty;

    public ProviderValidationRunnerTests()
    {
        Environment.SetEnvironmentVariable(TestSecretVariable, "test-key-value");
    }

    public void Dispose() => Environment.SetEnvironmentVariable(TestSecretVariable, _originalKey);

    private static ModelProfile DeepSeekProfile(string displayName = "DeepSeek Coder") =>
        ModelProfile.Register(ProviderKind.DeepSeek, displayName, "deepseek-chat", EndpointKind.ChatCompletions, DateTimeOffset.UtcNow);

    private static ProviderSecretReference Secret() => new(TestSecretVariable);

    private static ProviderValidationRunner Runner(StubHttpMessageHandler handler, TimeSpan? timeout = null) =>
        new(new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(10) });

    private const string SuccessSse =
        "data: {\"choices\":[{\"delta\":{\"content\":\"validation-ok\"},\"finish_reason\":null}]}\n\n" +
        "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"echo_tool\",\"arguments\":\"{\\\"text\\\":\\\"ok\\\"}\"}}]}}]}\n\n" +
        "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":3}}\n\n" +
        "data: [DONE]\n\n";

    [Fact]
    public async Task Successful_stream_reports_streaming_and_tool_calling()
    {
        var handler = StubHttpMessageHandler.Sse(SuccessSse);

        var result = await Runner(handler).ValidateAsync(
            "https://fake.test/v1", DeepSeekProfile(), Secret(), CancellationToken.None);

        Assert.True(result.Streaming);
        Assert.True(result.ToolCalling);
        Assert.Equal(ProviderErrorCategory.None, result.Error.Category);
        Assert.True(result.LatencyMs >= 0);
    }

    [Fact]
    public async Task Request_translation_is_canonical_and_secret_free()
    {
        var handler = StubHttpMessageHandler.Sse(SuccessSse);
        var runner = Runner(handler);

        await runner.ValidateAsync("https://fake.test/v1", DeepSeekProfile(), Secret(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://fake.test/v1/chat/completions", request.Request.RequestUri!.ToString());
        Assert.Equal("test-key-value", StubHttpMessageHandler.BearerToken(request));

        var body = StubHttpMessageHandler.ReadRequestBody(request);
        Assert.Contains("\"model\":\"deepseek-chat\"", body, StringComparison.Ordinal);
        Assert.Contains("\"stream\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"echo_tool\"", body, StringComparison.Ordinal);
        Assert.Contains("validation-ok", body, StringComparison.Ordinal);
        Assert.DoesNotContain("test-key-value", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fragmented_streams_are_handled_identically()
    {
        var fragmented =
            "data: {\"choices\":[{\"delta\":{\"content\":\"validation-\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":3}}\n\n" +
            "data: [DONE]\n\n";

        var handler = StubHttpMessageHandler.Sse(fragmented);
        var result = await Runner(handler).ValidateAsync(
            "https://fake.test/v1", DeepSeekProfile(), Secret(), CancellationToken.None);

        Assert.True(result.Streaming);
        Assert.Equal(ProviderErrorCategory.None, result.Error.Category);
    }

    [Fact]
    public async Task Malformed_stream_frame_fails_safely()
    {
        var handler = StubHttpMessageHandler.Sse("data: {broken\n\n");

        var result = await Runner(handler).ValidateAsync(
            "https://fake.test/v1", DeepSeekProfile(), Secret(), CancellationToken.None);

        Assert.Equal(ProviderErrorCategory.Malformed, result.Error.Category);
        Assert.DoesNotContain("broken", result.Error.SafeMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, ProviderErrorCategory.Unavailable)]
    [InlineData(HttpStatusCode.Unauthorized, ProviderErrorCategory.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, ProviderErrorCategory.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderErrorCategory.RateLimited)]
    public async Task Http_error_statuses_map_to_safe_categories(HttpStatusCode status, ProviderErrorCategory expected)
    {
        var handler = StubHttpMessageHandler.JsonError(status, """{"error":{"message":"boom"}}""");

        var result = await Runner(handler).ValidateAsync(
            "https://fake.test/v1", DeepSeekProfile(), Secret(), CancellationToken.None);

        Assert.Equal(expected, result.Error.Category);
        Assert.DoesNotContain("boom", result.Error.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_requests_carry_a_bounded_provider_message()
    {
        var handler = StubHttpMessageHandler.JsonError(HttpStatusCode.BadRequest, """{"error":{"message":"model unknown"}}""");

        var result = await Runner(handler).ValidateAsync(
            "https://fake.test/v1", DeepSeekProfile(), Secret(), CancellationToken.None);

        Assert.Equal(ProviderErrorCategory.InvalidRequest, result.Error.Category);
        Assert.Equal("model unknown", result.Error.SafeMessage);
    }

    [Fact]
    public async Task Slow_provider_times_out_with_a_safe_error()
    {
        var handler = StubHttpMessageHandler.Delay(TimeSpan.FromSeconds(3));

        var result = await Runner(handler, timeout: TimeSpan.FromMilliseconds(250)).ValidateAsync(
            "https://fake.test/v1", DeepSeekProfile(), Secret(), CancellationToken.None);

        Assert.Equal(ProviderErrorCategory.Timeout, result.Error.Category);
    }

    [Fact]
    public async Task Cancellation_propagates_to_the_caller()
    {
        var handler = StubHttpMessageHandler.Delay(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Runner(handler).ValidateAsync("https://fake.test/v1", DeepSeekProfile(), Secret(), cts.Token));
    }

    [Fact]
    public async Task Unsupported_endpoint_kinds_fail_before_any_transport()
    {
        var handler = StubHttpMessageHandler.Sse(SuccessSse);
        var profile = ModelProfile.Register(
            ProviderKind.DeepSeek, "D", "deepseek-chat", EndpointKind.Responses, DateTimeOffset.UtcNow);

        var result = await Runner(handler).ValidateAsync("https://fake.test/v1", profile, Secret(), CancellationToken.None);

        Assert.Equal(ProviderErrorCategory.Unsupported, result.Error.Category);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Missing_secret_fails_before_any_transport()
    {
        var handler = StubHttpMessageHandler.Sse(SuccessSse);
        var runner = Runner(handler);
        var secret = new ProviderSecretReference("SHARPAGENT_TEST_UNSET_VARIABLE");

        var result = await runner.ValidateAsync("https://fake.test/v1", DeepSeekProfile(), secret, CancellationToken.None);

        Assert.Equal(ProviderErrorCategory.InvalidRequest, result.Error.Category);
        Assert.Contains("not configured", result.Error.SafeMessage, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OpenCode_Go_adapter_rejects_unapproved_models_before_transport()
    {
        var handler = StubHttpMessageHandler.Sse(SuccessSse);
        var adapter = new OpenCodeGoAdapter(Runner(handler));
        var profile = ModelProfile.Register(
            ProviderKind.OpenCodeGo, "Not An Approved Model", "provider-id", EndpointKind.ChatCompletions, DateTimeOffset.UtcNow);

        var result = await adapter.ValidateAsync(profile, Secret(), CancellationToken.None);

        Assert.Equal(ProviderErrorCategory.InvalidRequest, result.Error.Category);
        Assert.Contains("not an approved OpenCode Go Plan model", result.Error.SafeMessage, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OpenCode_Go_adapter_forwards_approved_models_to_the_endpoint()
    {
        var handler = StubHttpMessageHandler.Sse(SuccessSse);
        var adapter = new OpenCodeGoAdapter(Runner(handler));
        var profile = ModelProfile.Register(
            ProviderKind.OpenCodeGo, "Ox Alpha Free", "provider-id", EndpointKind.ChatCompletions, DateTimeOffset.UtcNow);

        var result = await adapter.ValidateAsync(profile, Secret(), CancellationToken.None);

        Assert.True(result.Streaming);
        Assert.Single(handler.Requests);
    }
}
