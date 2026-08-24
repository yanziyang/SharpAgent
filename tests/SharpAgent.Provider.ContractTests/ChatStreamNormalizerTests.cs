using SharpAgent.Application.Abstractions;
using SharpAgent.Providers.Common;
using Xunit;

namespace SharpAgent.Provider.ContractTests;

/// <summary>
/// Deterministic normalization contracts for the canonical provider stream
/// (FR-054): text deltas, tool calls, finish reasons, usage and malformed frames.
/// Fixtures are recorded, sanitized payloads; no live provider is involved.
/// </summary>
public sealed class ChatStreamNormalizerTests
{
    [Fact]
    public void Text_deltas_are_normalized_in_order()
    {
        var result = ChatStreamNormalizer.Normalize(
        [
            """{"choices":[{"delta":{"content":"Hello"},"finish_reason":null}]}""",
            """{"choices":[{"delta":{"content":" world"},"finish_reason":null}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""",
        ]);

        Assert.Null(result.Error);
        Assert.Equal(
            ["Hello", " world"],
            result.Fragments
                .Where(static fragment => fragment.Kind == StreamFragmentKind.TextDelta)
                .Select(static fragment => fragment.Text));
        Assert.Contains(
            result.Fragments,
            static fragment => fragment.Kind == StreamFragmentKind.Finish && fragment.FinishReason == "stop");
    }

    [Fact]
    public void Tool_call_arguments_split_across_chunks_are_accumulated()
    {
        var result = ChatStreamNormalizer.Normalize(
        [
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"echo_tool","arguments":""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"text\":"}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"ok\"}"}}]}}]}""",
        ]);

        Assert.Null(result.Error);
        var calls = result.Fragments
            .Where(static fragment => fragment.Kind == StreamFragmentKind.ToolCall)
            .Select(static fragment => fragment.ToolCall)
            .ToList();

        Assert.Equal("echo_tool", calls[0]!.Name);
        Assert.Equal("call_1", calls[0]!.Id);
        Assert.Equal("""{"text":"ok"}""", calls[^1]!.ArgumentsJson);
    }

    [Fact]
    public void Usage_chunks_are_normalized_to_token_counts()
    {
        var result = ChatStreamNormalizer.Normalize(
        [
            """{"choices":[{"delta":{"content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":5,"total_tokens":17}}""",
        ]);

        Assert.Null(result.Error);
        var usage = Assert.Single(
            result.Fragments,
            static fragment => fragment.Kind == StreamFragmentKind.Usage).Usage;

        Assert.Equal(12, usage!.InputTokens);
        Assert.Equal(5, usage.OutputTokens);
    }

    [Fact]
    public void Malformed_frames_fail_with_a_safe_error()
    {
        var result = ChatStreamNormalizer.Normalize(["{not valid json"]);

        Assert.NotNull(result.Error);
        Assert.Equal(ProviderErrorCategory.Malformed, result.Error.Category);
        Assert.DoesNotContain("{not", result.Error.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_and_done_frames_are_ignored()
    {
        var result = ChatStreamNormalizer.Normalize(["", "[DONE]"]);

        Assert.Null(result.Error);
        Assert.Empty(result.Fragments);
    }

    [Fact]
    public void Multiple_choices_are_all_normalized()
    {
        var result = ChatStreamNormalizer.Normalize(
        [
            """{"choices":[{"delta":{"content":"a"}},{"delta":{"content":"b"}}]}""",
        ]);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Fragments.Count(static fragment => fragment.Kind == StreamFragmentKind.TextDelta));
    }

    [Fact]
    public void Usage_only_frames_without_choices_are_normalized()
    {
        var result = ChatStreamNormalizer.Normalize(
        [
            """{"usage":{"prompt_tokens":5,"completion_tokens":2}}""",
        ]);

        Assert.Null(result.Error);
        var usage = Assert.Single(
            result.Fragments,
            static fragment => fragment.Kind == StreamFragmentKind.Usage).Usage;

        Assert.Equal(5, usage!.InputTokens);
        Assert.Equal(2, usage.OutputTokens);
    }

    [Fact]
    public void Null_chunks_are_skipped_without_error()
    {
        var result = ChatStreamNormalizer.Normalize(["null"]);

        Assert.Null(result.Error);
        Assert.Empty(result.Fragments);
    }

    [Fact]
    public void Delta_level_finish_reasons_are_normalized()
    {
        var result = ChatStreamNormalizer.Normalize(
        [
            """{"choices":[{"delta":{"content":"x","finish_reason":"length"}}]}""",
        ]);

        Assert.Null(result.Error);
        Assert.Contains(
            result.Fragments,
            static fragment => fragment.Kind == StreamFragmentKind.Finish && fragment.FinishReason == "length");
    }
}
