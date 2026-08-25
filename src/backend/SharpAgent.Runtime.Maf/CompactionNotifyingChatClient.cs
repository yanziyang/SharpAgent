using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace SharpAgent.Runtime.Maf;

/// <summary>
/// Wraps the summarizer chat client so the runtime can observe when a
/// summarization-compaction pass actually ran, and emit the canonical
/// <c>context_compacted</c> event (functional spec 9.3).
/// </summary>
public sealed class CompactionNotifyingChatClient(IChatClient inner) : IChatClient
{
    public bool SummarizationInvoked { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        SummarizationInvoked = true;
        return inner.GetResponseAsync(messages, options, cancellationToken);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        SummarizationInvoked = true;
        await foreach (var update in inner.GetStreamingResponseAsync(messages, options, cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        inner.GetService(serviceType, serviceKey);

    public void Dispose() => inner.Dispose();
}
