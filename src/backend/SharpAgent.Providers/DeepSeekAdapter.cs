using Microsoft.Extensions.AI;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Profiles;
using SharpAgent.Providers.Common;

namespace SharpAgent.Providers;

/// <summary>
/// DeepSeek adapter. Uses the server-side compatible OpenAI-style endpoint and
/// normalizes stream frames, tool requests, rate/provider errors and usage to the
/// canonical contract (FR-054).
/// </summary>
public sealed class DeepSeekAdapter(ProviderValidationRunner runner) : IModelProviderAdapter
{
    public const string BaseUrlVariable = "SHARPAGENT_DEEPSEEK_BASE_URL";

    public const string DefaultBaseUrl = "https://api.deepseek.com";

    public ProviderKind Provider => ProviderKind.DeepSeek;

    public Task<ProfileValidationResult> ValidateAsync(
        ModelProfile profile,
        ProviderSecretReference secretReference,
        CancellationToken cancellationToken) =>
        runner.ValidateAsync(
            Environment.GetEnvironmentVariable(BaseUrlVariable) ?? DefaultBaseUrl,
            profile,
            secretReference,
            cancellationToken);

    public IChatClient CreateChatClient(
        ModelProfile profile,
        ProviderSecretReference secretReference) =>
        ChatClientFactory.Create(
            Environment.GetEnvironmentVariable(BaseUrlVariable) ?? DefaultBaseUrl,
            profile,
            secretReference);
}
