using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Profiles;
using SharpAgent.Providers.Common;

namespace SharpAgent.Providers;

/// <summary>
/// OpenRouter adapter. Normalizes the OpenAI-compatible stream, router/provider
/// errors and usage; selection gating keeps it plan-only until a validation run
/// records successful compatible behavior (design section 7.2).
/// </summary>
public sealed class OpenRouterAdapter(ProviderValidationRunner runner) : IModelProviderAdapter
{
    public const string BaseUrlVariable = "SHARPAGENT_OPENROUTER_BASE_URL";

    public const string DefaultBaseUrl = "https://openrouter.ai/api/v1";

    public ProviderKind Provider => ProviderKind.OpenRouter;

    public Task<ProfileValidationResult> ValidateAsync(
        ModelProfile profile,
        ProviderSecretReference secretReference,
        CancellationToken cancellationToken) =>
        runner.ValidateAsync(
            Environment.GetEnvironmentVariable(BaseUrlVariable) ?? DefaultBaseUrl,
            profile,
            secretReference,
            cancellationToken);
}
