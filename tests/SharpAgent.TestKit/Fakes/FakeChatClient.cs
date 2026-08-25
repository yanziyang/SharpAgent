using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace SharpAgent.TestKit.Fakes;

/// <summary>
/// Deterministic scripted chat client for the MAF runtime adapter. Each step is a
/// list of contents the client streams back. It never performs real I/O; MAF's
/// agent layer drives registered tools around these scripts.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Queue<IReadOnlyList<AIContent>> _script = new();

    public int GetResponseCalls { get; private set; }

    public int StreamingCalls { get; private set; }

    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    public FakeChatClient Step(params AIContent[] contents)
    {
        _script.Enqueue(contents);
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        GetResponseCalls++;
        LastMessages = [.. messages];
        cancellationToken.ThrowIfCancellationRequested();

        var contents = _script.Count > 0 ? _script.Dequeue() : [];
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [.. contents])));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        StreamingCalls++;
        LastMessages = [.. messages];

        var contents = _script.Count > 0 ? _script.Dequeue() : [];
        if (contents.Count == 0)
        {
            yield break;
        }

        foreach (var delay in contents.OfType<DelayContent>())
        {
            await Task.Delay(delay.Duration, cancellationToken).ConfigureAwait(false);
        }

        var streamable = contents.Where(static content => content is not DelayContent).ToArray();
        if (streamable.Length == 0)
        {
            yield break;
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, [.. streamable]);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    public static TextContent Text(string text) => new(text);

    public static FunctionCallContent Call(string name, string argumentsJson)
    {
        using var document = System.Text.Json.JsonDocument.Parse(argumentsJson);
        var arguments = document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
            ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson)
            : new Dictionary<string, object?> { ["arguments"] = document.RootElement.Clone() };

        return new FunctionCallContent(Guid.NewGuid().ToString("N"), name, arguments);
    }

    public static FunctionResultContent Result(string callId, object result) =>
        new(callId, result);

    public static UsageContent Usage(int input, int output) =>
            new()
            {
                Details = new UsageDetails { InputTokenCount = input, OutputTokenCount = output },
            };

    public static DelayContent Delay(long milliseconds) => new(milliseconds);

    /// <summary>Scripted pacing sentinel; simulates a slow provider.</summary>
    public sealed class DelayContent(long milliseconds) : AIContent
    {
        public TimeSpan Duration { get; } = TimeSpan.FromMilliseconds(milliseconds);
    }
}
