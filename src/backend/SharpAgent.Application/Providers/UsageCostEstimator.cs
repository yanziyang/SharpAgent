using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Profiles;

namespace SharpAgent.Application.Providers;

/// <summary>
/// Token-usage cost estimation against profile price metadata. Providers report
/// token counts; SharpAgent owns the conversion to a cost estimate for run-limit
/// enforcement (FR-052). Returns null when either side is unknown.
/// </summary>
public static class UsageCostEstimator
{
    /// <summary>Estimates the cost of one usage report from prices in USD per million tokens.</summary>
    public static decimal? Estimate(
        (decimal InputUsdPerMillion, decimal OutputUsdPerMillion) prices,
        int inputTokens,
        int outputTokens)
    {
        return Math.Round(
            inputTokens / 1_000_000m * prices.InputUsdPerMillion
            + outputTokens / 1_000_000m * prices.OutputUsdPerMillion,
            6);
    }

    public static decimal? Estimate(ModelProfile profile, NormalizedUsage usage)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(usage);

        if (usage.InputTokens is null || usage.OutputTokens is null)
        {
            return null;
        }

        var capabilities = profile.GetCapabilities();
        if (capabilities.EstimatedUsdPerMillionInputTokens is null
            || capabilities.EstimatedUsdPerMillionOutputTokens is null)
        {
            return null;
        }

        return Math.Round(
            usage.InputTokens.Value / 1_000_000m * capabilities.EstimatedUsdPerMillionInputTokens.Value
            + usage.OutputTokens.Value / 1_000_000m * capabilities.EstimatedUsdPerMillionOutputTokens.Value,
            6);
    }
}
