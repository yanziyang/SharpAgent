using Microsoft.Extensions.AI;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Profiles;

namespace SharpAgent.TestKit.Fakes;

/// <summary>
/// Deterministic in-process provider adapter for offline tests and the fake
/// runtime. Records every validation invocation so tests can assert the
/// allowlist gate ran before any transport.
/// </summary>
public sealed class FakeProviderAdapter : IModelProviderAdapter
{
    private readonly Func<ModelProfile, ProviderSecretReference, ProfileValidationResult> _handler;
    private readonly Func<IChatClient>? _chatClientFactory;

    public FakeProviderAdapter(
        ProviderKind provider = ProviderKind.Fake,
        Func<ModelProfile, ProviderSecretReference, ProfileValidationResult>? handler = null,
        Func<IChatClient>? chatClientFactory = null)
    {
        Provider = provider;
        _handler = handler ?? DefaultSuccess;
        _chatClientFactory = chatClientFactory;
    }

    private static ProfileValidationResult DefaultSuccess(
        ModelProfile profile, ProviderSecretReference secret) => new(
        Streaming: true,
        ToolCalling: true,
        ContextWindowTokens: 64_000,
        LatencyMs: 12,
        Error: NormalizedProviderError.None);

    public ProviderKind Provider { get; }

    public List<(ModelProfile Profile, ProviderSecretReference Secret)> Invocations { get; } = [];

    public Task<ProfileValidationResult> ValidateAsync(
        ModelProfile profile,
        ProviderSecretReference secretReference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Invocations.Add((profile, secretReference));
        return Task.FromResult(_handler(profile, secretReference));
    }

    public IChatClient CreateChatClient(
        ModelProfile profile,
        ProviderSecretReference secretReference) =>
        _chatClientFactory?.Invoke()
        ?? throw new InvalidOperationException("No fake chat client is configured for this adapter.");
}

public sealed class FakeProviderAdapterRegistry(params IModelProviderAdapter[] adapters) : IProviderAdapterRegistry
{
    public IModelProviderAdapter? Find(ProviderKind provider) =>
            adapters.FirstOrDefault(adapter => adapter.Provider == provider);
}
