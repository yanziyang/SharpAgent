using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Providers;
using SharpAgent.Domain.Profiles;
using Xunit;

namespace SharpAgent.Application.Tests.Providers;

public sealed class UsageCostEstimatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    private static ModelProfile ProfileWithPrices(decimal inputUsd, decimal outputUsd)
    {
        var profile = ModelProfile.Register(
            ProviderKind.Fake, "P", "id", EndpointKind.ChatCompletions, Now);
        profile.MarkValidated(
            new ProfileCapabilities(true, true, 64_000, inputUsd, outputUsd), "ok", Now);
        return profile;
    }

    [Fact]
    public void Estimates_cost_from_tokens_and_profile_prices()
    {
        var profile = ProfileWithPrices(0.50m, 1.50m);

        var estimate = UsageCostEstimator.Estimate(profile, new NormalizedUsage(1_000_000, 2_000_000, null));

        Assert.Equal(3.50m, estimate);
    }

    [Fact]
    public void Rounds_to_six_decimals()
    {
        var profile = ProfileWithPrices(0.5m, 0.25m);

        var estimate = UsageCostEstimator.Estimate(profile, new NormalizedUsage(1, 2, null));

        Assert.Equal(0.000001m, estimate);
    }

    [Fact]
    public void Missing_token_counts_yield_no_estimate()
    {
        var profile = ProfileWithPrices(0.50m, 1.50m);

        Assert.Null(UsageCostEstimator.Estimate(profile, NormalizedUsage.None));
        Assert.Null(UsageCostEstimator.Estimate(profile, new NormalizedUsage(1, null, null)));
        Assert.Null(UsageCostEstimator.Estimate(profile, new NormalizedUsage(null, 1, null)));
    }

    [Fact]
    public void Missing_price_metadata_yields_no_estimate()
    {
        var profile = ModelProfile.Register(
            ProviderKind.Fake, "P", "id", EndpointKind.ChatCompletions, Now);
        profile.MarkValidated(new ProfileCapabilities(true, true, 64_000, null, null), "ok", Now);

        Assert.Null(UsageCostEstimator.Estimate(profile, new NormalizedUsage(1_000, 2_000, null)));
    }
}
