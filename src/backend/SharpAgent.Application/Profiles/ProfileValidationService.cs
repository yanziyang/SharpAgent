using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Common;
using SharpAgent.Application.Providers;
using SharpAgent.Domain.Profiles;

namespace SharpAgent.Application.Profiles;

/// <summary>
/// Model-profile validation command (Implementation Plan section 10.1, design
/// section 7.4): resolves the server-side secret reference, runs a bounded
/// non-destructive stream/tool-schema check through the provider adapter, and
/// persists only safe capability metadata. Never touches a workspace.
/// </summary>
public sealed class ProfileValidationService(
    IModelProfileRepository profiles,
    IProviderAdapterRegistry adapters,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<ProfileValidationResult> ValidateAsync(
        string modelProfileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelProfileId);

        var profile = await profiles.FindAsync(modelProfileId, cancellationToken).ConfigureAwait(false)
                              ?? throw new NotFoundException("model profile", modelProfileId);

        // Allowlist gate runs BEFORE any adapter lookup or outbound transport (FR-053, plan 4.2).
        if (profile.Provider == ProviderKind.OpenCodeGo && !OpenCodeGoPlanAllowlist.IsAllowed(profile.DisplayName))
        {
            var message = OpenCodeGoPlanAllowlist.SafeMessage(profile.DisplayName);
            return await PersistFailureAsync(profile, message, cancellationToken).ConfigureAwait(false);
        }

        var adapter = adapters.Find(profile.Provider)
                      ?? throw new ConflictException(
                          "unsupported_provider",
                          $"No adapter is registered for provider '{profile.Provider}'.");

        var secretReference = new ProviderSecretReference(
            string.IsNullOrWhiteSpace(profile.ConfigReference)
                ? DefaultSecretVariable(profile.Provider)
                : profile.ConfigReference);

        ProfileValidationResult result;
        try
        {
            result = await adapter.ValidateAsync(profile, secretReference, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            result = ProfileValidationResult.Failed(
                new NormalizedProviderError(ProviderErrorCategory.Unavailable, "The provider could not be reached."));
        }

        if (result.Error.Category == ProviderErrorCategory.None)
        {
            profile.MarkValidated(
                new ProfileCapabilities(
                    result.Streaming,
                    result.ToolCalling,
                    result.ContextWindowTokens,
                    profile.GetCapabilities().EstimatedUsdPerMillionInputTokens,
                    profile.GetCapabilities().EstimatedUsdPerMillionOutputTokens),
                safeMessage: "Provider validation completed successfully.",
                nowUtc: clock.UtcNow);
        }
        else
        {
            await PersistFailureAsync(profile, result.Error.SafeMessage, cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task<ProfileValidationResult> PersistFailureAsync(
        ModelProfile profile,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        profile.MarkValidationFailed(safeMessage, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ProfileValidationResult.Failed(new NormalizedProviderError(ProviderErrorCategory.InvalidRequest, safeMessage));
    }

    private static string DefaultSecretVariable(ProviderKind provider) => provider switch
    {
        ProviderKind.OpenCodeGo => "SHARPAGENT_OPENCODE_GO_API_KEY",
        ProviderKind.DeepSeek => "SHARPAGENT_DEEPSEEK_API_KEY",
        ProviderKind.OpenRouter => "SHARPAGENT_OPENROUTER_API_KEY",
        ProviderKind.Fake => "SHARPAGENT_FAKE_API_KEY",
        _ => "SHARPAGENT_PROVIDER_API_KEY",
    };
}
