using SharpAgent.Domain.Common;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Policies;
using Xunit;

namespace SharpAgent.Domain.Tests.Entities;

public sealed class ModelProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    private static ModelProfile NewProfile() =>
        ModelProfile.Register(ProviderKind.OpenCodeGo, "Ox Alpha Free", "resolved-locally", EndpointKind.ChatCompletions, Now);

    [Fact]
    public void Register_requires_names()
    {
        Assert.Throws<ArgumentException>(
            () => ModelProfile.Register(ProviderKind.DeepSeek, string.Empty, "id", EndpointKind.ChatCompletions, Now));
        Assert.Throws<ArgumentException>(
            () => ModelProfile.Register(ProviderKind.DeepSeek, "DeepSeek Coder", string.Empty, EndpointKind.ChatCompletions, Now));
    }

    [Fact]
    public void Execute_requires_enabled_validated_streaming_tool_profile()
    {
        var profile = NewProfile();

        Assert.False(profile.CanExecute());
        Assert.False(profile.CanPlan()); // disabled

        profile.Enable(Now);
        Assert.False(profile.CanExecute());      // still unvalidated -> no tools
        Assert.True(profile.CanPlan());          // plan-only until validated (E2E-08)

        profile.MarkValidated(
            new ProfileCapabilities(true, true, 128_000, 0.5m, 1.5m), "ok", Now.AddMinutes(1));

        Assert.True(profile.CanExecute());
        Assert.True(profile.CanPlan());
        Assert.Equal(128_000, profile.GetCapabilities().ContextWindowTokens);
    }

    [Fact]
    public void Streaming_only_profiles_cannot_execute_tools()
    {
        var profile = NewProfile();
        profile.Enable(Now);
        profile.MarkValidated(new ProfileCapabilities(true, false, 64_000, null, null), "stream only", Now);

        Assert.False(profile.CanExecute());
        Assert.True(profile.CanPlan());
    }

    [Fact]
    public void Failed_validation_blocks_both_modes_and_disable_blocks_everything()
    {
        var failed = NewProfile();
        failed.Enable(Now);
        failed.MarkValidationFailed("Provider rejected the smoke request.", Now);
        Assert.False(failed.CanPlan());
        Assert.False(failed.CanExecute());

        var disabled = NewProfile();
        disabled.Enable(Now);
        disabled.MarkValidated(new ProfileCapabilities(true, true, null, null, null), "ok", Now);
        disabled.Disable(Now.AddMinutes(1));
        Assert.False(disabled.CanExecute());
    }
}

public sealed class PolicyProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    private static PolicyProfile NewPolicy() => PolicyProfile.Define(
        "default-controlled", 45, 40, 5.00m, 10, Now);

    [Fact]
    public void Define_validates_limits()
    {
        Assert.Throws<ArgumentException>(() => PolicyProfile.Define(string.Empty, 1, 1, 1m, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolicyProfile.Define("p", 0, 1, 1m, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolicyProfile.Define("p", 1, 0, 1m, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolicyProfile.Define("p", 1, 1, 0m, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => PolicyProfile.Define("p", 1, 1, 1m, 0, Now));
    }

    [Fact]
    public void Overrides_may_only_tighten_policy_limits()
    {
        var policy = NewPolicy();

        Assert.Equal(30, policy.ApplyDurationOverride(30));   // tighter kept
        Assert.Equal(45, policy.ApplyDurationOverride(60));   // clamped to policy
        Assert.Equal(45, policy.ApplyDurationOverride(null)); // default

        Assert.Equal(20, policy.ApplyToolCallOverride(20));
        Assert.Equal(40, policy.ApplyToolCallOverride(null));
        Assert.Equal(40, policy.ApplyToolCallOverride(100));
        Assert.Equal(2.50m, policy.ApplyCostOverride(2.50m));
        Assert.Equal(5.00m, policy.ApplyCostOverride(50m));
    }

    [Fact]
    public void Default_rules_encode_the_controlled_mvp_matrix()
    {
        var policy = NewPolicy();

        Assert.Contains("\"apply_patch\":\"require_approval\"", policy.RulesJson, StringComparison.Ordinal);
        Assert.Contains("\"delete\":\"deny\"", policy.RulesJson, StringComparison.Ordinal);
        Assert.Contains("\"read_file\":\"allow\"", policy.RulesJson, StringComparison.Ordinal);
    }
}

public sealed class ValidatedThenDisabledProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Disabling_after_validation_blocks_both_modes()
    {
        var profile = ModelProfile.Register(ProviderKind.Fake, "P", "id", EndpointKind.None, Now);
        profile.Enable(Now);
        profile.MarkValidated(new ProfileCapabilities(true, true, null, null, null), "ok", Now);
        profile.Disable(Now.AddMinutes(1));

        Assert.False(profile.CanPlan());
        Assert.False(profile.CanExecute());
    }
}

