using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Providers;
using SharpAgent.Domain.Profiles;
using SharpAgent.Providers.Common;

namespace SharpAgent.Providers;

/// <summary>
/// OpenCode Go adapter (FR-053). The ChatCompletions endpoint style is supported
/// in this slice; other styles fail validation with a safe unsupported error.
/// The strict Plan allowlist (plan section 4.2) is enforced here AND in profile
/// validation, so no unapproved model ever reaches the outbound transport.
/// </summary>
public sealed class OpenCodeGoAdapter(ProviderValidationRunner runner) : IModelProviderAdapter
{
    public const string BaseUrlVariable = "SHARPAGENT_OPENCODE_GO_BASE_URL";

    public const string DefaultBaseUrl = "https://api.opencode.go/v1";

    public ProviderKind Provider => ProviderKind.OpenCodeGo;

    public Task<ProfileValidationResult> ValidateAsync(
        ModelProfile profile,
        ProviderSecretReference secretReference,
        CancellationToken cancellationToken)
    {
        if (!OpenCodeGoPlanAllowlist.IsAllowed(profile.DisplayName))
        {
            return Task.FromResult(ProfileValidationResult.Failed(new NormalizedProviderError(
                ProviderErrorCategory.InvalidRequest,
                OpenCodeGoPlanAllowlist.SafeMessage(profile.DisplayName))));
        }

        return runner.ValidateAsync(
            Environment.GetEnvironmentVariable(BaseUrlVariable) ?? DefaultBaseUrl,
            profile,
            secretReference,
            cancellationToken);
    }
}
