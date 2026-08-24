using System.Text.Json;
using System.Text.Json.Serialization;
using SharpAgent.Application.Abstractions;

namespace SharpAgent.Providers.Common;

/// <summary>
/// Normalizes OpenAI-compatible chat.completions SSE chunks into canonical stream
/// fragments (FR-054). Tool-call arguments arrive split across chunks; they are
/// accumulated per call index so the last fragment carries the complete JSON.
/// </summary>
public static class ChatStreamNormalizer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public sealed record NormalizationResult(
        IReadOnlyList<NormalizedStreamFragment> Fragments,
        NormalizedProviderError? Error);

    private sealed record JsonDelta(
            string? Content,
            [property: JsonPropertyName("tool_calls")] List<JsonToolCallDelta>? ToolCalls,
            [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record JsonToolCallDelta(int? Index, string? Id, string? Type, JsonFunction? Function);

    private sealed record JsonFunction(string? Name, string? Arguments);

    private sealed record JsonChunk(string? Id, List<JsonChoice>? Choices, JsonUsage? Usage);

    private sealed record JsonChoice(JsonDelta? Delta, [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record JsonUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens);

    /// <summary>Parses raw SSE data payloads (the <c>data:</c> values, without <c>[DONE]</c>).</summary>
    public static NormalizationResult Normalize(IReadOnlyList<string> dataFrames)
    {
        var fragments = new List<NormalizedStreamFragment>();
        var argumentsByIndex = new Dictionary<int, string>();

        foreach (var frame in dataFrames)
        {
            if (string.IsNullOrWhiteSpace(frame) || frame == "[DONE]")
            {
                continue;
            }

            JsonChunk? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<JsonChunk>(frame, Options);
            }
            catch (JsonException)
            {
                return new NormalizationResult(
                    fragments,
                    new NormalizedProviderError(ProviderErrorCategory.Malformed, "The provider sent a malformed stream frame."));
            }

            if (chunk is null)
            {
                continue;
            }

            if (chunk.Choices is null || chunk.Choices.Count == 0)
            {
                if (chunk.Usage is { } usage)
                {
                    fragments.Add(new NormalizedStreamFragment(
                        StreamFragmentKind.Usage,
                        null,
                        null,
                        null,
                        new NormalizedUsage(usage.PromptTokens, usage.CompletionTokens, null)));
                }

                continue;
            }

            foreach (var choice in chunk.Choices)
            {
                if (choice.Delta is { } delta)
                {
                    if (!string.IsNullOrEmpty(delta.Content))
                    {
                        fragments.Add(new NormalizedStreamFragment(
                            StreamFragmentKind.TextDelta, delta.Content, null, null, null));
                    }

                    if (delta.ToolCalls is not null)
                    {
                        foreach (var toolCall in delta.ToolCalls)
                        {
                            var index = toolCall.Index ?? 0;
                            if (toolCall.Function?.Name is { Length: > 0 } name)
                            {
                                fragments.Add(new NormalizedStreamFragment(
                                    StreamFragmentKind.ToolCall,
                                    null,
                                    new NormalizedToolCall(toolCall.Id ?? string.Empty, name, string.Empty),
                                    null,
                                    null));
                            }

                            if (toolCall.Function?.Arguments is { } arguments)
                            {
                                argumentsByIndex.TryGetValue(index, out var accumulated);
                                var combined = accumulated + arguments;
                                argumentsByIndex[index] = combined;
                                fragments.Add(new NormalizedStreamFragment(
                                    StreamFragmentKind.ToolCall,
                                    null,
                                    new NormalizedToolCall(toolCall.Id ?? string.Empty, string.Empty, combined),
                                    null,
                                    null));
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(delta.FinishReason))
                    {
                        fragments.Add(new NormalizedStreamFragment(
                            StreamFragmentKind.Finish, null, null, delta.FinishReason, null));
                    }
                }

                if (!string.IsNullOrEmpty(choice.FinishReason))
                {
                    fragments.Add(new NormalizedStreamFragment(
                        StreamFragmentKind.Finish, null, null, choice.FinishReason, null));
                }
            }

            if (chunk.Usage is { } usage2)
            {
                fragments.Add(new NormalizedStreamFragment(
                    StreamFragmentKind.Usage,
                    null,
                    null,
                    null,
                    new NormalizedUsage(usage2.PromptTokens, usage2.CompletionTokens, null)));
            }
        }

        return new NormalizationResult(fragments, null);
    }
}
