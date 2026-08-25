using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Profiles;
using AChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SharpAgent.Providers;

/// <summary>
/// Development-only deterministic provider used to make a fresh local checkout
/// usable without credentials or outbound network access. It is never registered
/// outside the explicitly enabled local-demo mode.
/// </summary>
public sealed class LocalDemoProviderAdapter : IModelProviderAdapter
{
    public ProviderKind Provider => ProviderKind.Fake;

    public Task<ProfileValidationResult> ValidateAsync(
        ModelProfile profile,
        ProviderSecretReference secretReference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(secretReference);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ProfileValidationResult(
            Streaming: true,
            ToolCalling: false,
            ContextWindowTokens: 16_000,
            LatencyMs: 1,
            Error: NormalizedProviderError.None));
    }

    public IChatClient CreateChatClient(
        ModelProfile profile,
        ProviderSecretReference secretReference)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(secretReference);
        return new LocalDemoChatClient();
    }
}

/// <summary>Small deterministic chat client for the local Plan-mode walkthrough.</summary>
internal sealed class LocalDemoChatClient : IChatClient
{
    private const string Summary =
        "Local demo plan complete. This deterministic profile made no external provider request and changed no files. Configure a validated provider profile for real repository analysis.";

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<AChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateResponse());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate(
            ChatRole.Assistant,
            [new TextContent(Summary), CreateUsage()]);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static ChatResponse CreateResponse() =>
        new(new AChatMessage(ChatRole.Assistant, [new TextContent(Summary), CreateUsage()]));

    private static UsageContent CreateUsage() => new()
    {
        Details = new UsageDetails
        {
            InputTokenCount = 32,
            OutputTokenCount = 32,
        },
    };
}
