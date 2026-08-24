using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;

namespace SharpAgent.Application.Profiles;

/// <summary>Safe model-profile projection for selectors and settings screens (FR-051).</summary>
public sealed record ModelProfileDto(
    string Id,
    string Provider,
    string DisplayName,
    bool Enabled,
    string ValidationStatus,
    bool Streaming,
    bool ToolCalling,
    int? ContextWindowTokens,
    decimal? EstimatedUsdPerMillionInputTokens,
    decimal? EstimatedUsdPerMillionOutputTokens,
    bool EligibleForPlan,
    bool EligibleForExecute);

public sealed record PolicyProfileDto(
    string Id,
    string Name,
    int MaxRunDurationMinutes,
    int MaxToolCalls,
    decimal MaxEstimatedCostUsd,
    int ApprovalExpiryMinutes);

public sealed class CatalogService(
    IModelProfileRepository modelProfiles,
    IPolicyProfileRepository policies)
{
    public async Task<IReadOnlyList<ModelProfileDto>> ListModelProfilesAsync(CancellationToken cancellationToken = default)
    {
        var list = await modelProfiles.ListAsync(cancellationToken).ConfigureAwait(false);
        return [.. list.OrderBy(static profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase).Select(Project)];
    }

    public async Task<IReadOnlyList<PolicyProfileDto>> ListPolicyProfilesAsync(CancellationToken cancellationToken = default)
    {
        var list = await policies.ListAsync(cancellationToken).ConfigureAwait(false);
        return [.. list.OrderBy(static policy => policy.Name, StringComparer.OrdinalIgnoreCase).Select(ProjectPolicy)];
    }

    public static ModelProfileDto Project(ModelProfile profile)
    {
        var capabilities = profile.GetCapabilities();

        return new ModelProfileDto(
            profile.Id,
            profile.Provider.ToString(),
            profile.DisplayName,
            profile.Enabled,
            profile.ValidationStatus.ToString(),
            capabilities.Streaming,
            capabilities.ToolCalling,
            capabilities.ContextWindowTokens,
            capabilities.EstimatedUsdPerMillionInputTokens,
            capabilities.EstimatedUsdPerMillionOutputTokens,
            profile.CanPlan(),
            profile.CanExecute());
    }

    public static PolicyProfileDto ProjectPolicy(PolicyProfile policy) => new(
        policy.Id,
        policy.Name,
        policy.MaxRunDurationMinutes,
        policy.MaxToolCalls,
        policy.MaxEstimatedCostUsd,
        policy.ApprovalExpiryMinutes);
}

