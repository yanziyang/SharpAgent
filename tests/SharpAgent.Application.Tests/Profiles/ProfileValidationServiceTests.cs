using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Common;
using SharpAgent.Application.Profiles;
using SharpAgent.Domain.Profiles;
using SharpAgent.TestKit.Fakes;
using Xunit;

namespace SharpAgent.Application.Tests.Profiles;

/// <summary>
/// Profile validation command: safe metadata persistence, allowlist gating
/// BEFORE any outbound transport, and browser-safe projection (FR-051, FR-052).
/// </summary>
public sealed class ProfileValidationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly MemoryModelProfileRepository _profiles = new();
    private readonly FakeProviderAdapter _adapter = new();
    private FakeProviderAdapterRegistry _registry;
    private readonly PassThroughUnitOfWork _unitOfWork = new();
    private readonly FakeClock _clock = new(Now);
    private readonly ProfileValidationService _service;

    public ProfileValidationServiceTests()
    {
        _registry = new FakeProviderAdapterRegistry(_adapter);
        _service = new ProfileValidationService(_profiles, _registry, _unitOfWork, _clock);
    }

    private ModelProfile SeedProfile(ProviderKind provider = ProviderKind.Fake, string displayName = "Fake Model")
    {
        var profile = ModelProfile.Register(
            provider, displayName, "fake-model-id", EndpointKind.ChatCompletions, Now);
        _profiles.Seed(profile);
        return profile;
    }

    [Fact]
    public async Task Successful_validation_persists_capabilities_and_validated_status()
    {
        var profile = SeedProfile();

        var result = await _service.ValidateAsync(profile.Id, CancellationToken.None);

        Assert.True(result.Streaming);
        Assert.True(result.ToolCalling);
        Assert.Equal(ValidationStatus.Validated, profile.ValidationStatus);
        Assert.Equal(64_000, profile.GetCapabilities().ContextWindowTokens);

        // Validation records capabilities; enabling remains an operator action.
        profile.Enable(Now);
        Assert.True(profile.CanExecute());
        Assert.True(_unitOfWork.SaveCalls > 0);
    }

    [Fact]
    public async Task Failed_validation_marks_the_profile_failed_with_a_safe_message()
    {
        var failing = new FakeProviderAdapter(handler: (_, _) => ProfileValidationResult.Failed(
            new NormalizedProviderError(ProviderErrorCategory.RateLimited, "rate limit reached")));
        _registry = new FakeProviderAdapterRegistry(failing);
        var service = new ProfileValidationService(_profiles, _registry, _unitOfWork, _clock);
        var profile = SeedProfile();

        var result = await service.ValidateAsync(profile.Id, CancellationToken.None);

        Assert.Equal(ProviderErrorCategory.RateLimited, result.Error.Category);
        Assert.Equal(ValidationStatus.Failed, profile.ValidationStatus);
        Assert.False(profile.CanPlan());
        Assert.False(profile.CanExecute());
    }

    [Fact]
    public async Task Unapproved_open_code_go_models_are_rejected_before_any_transport()
    {
        var profile = SeedProfile(ProviderKind.OpenCodeGo, "Not An Approved Model");

        var result = await _service.ValidateAsync(profile.Id, CancellationToken.None);

        Assert.Equal(ProviderErrorCategory.InvalidRequest, result.Error.Category);
        Assert.Contains("not an approved OpenCode Go Plan model", result.Error.SafeMessage, StringComparison.Ordinal);
        Assert.Empty(_adapter.Invocations);
        Assert.Equal(ValidationStatus.Failed, profile.ValidationStatus);
    }

    [Fact]
    public async Task Approved_open_code_go_models_reach_the_adapter()
    {
        var adapter = new FakeProviderAdapter(ProviderKind.OpenCodeGo);
        _registry = new FakeProviderAdapterRegistry(adapter);
        var service = new ProfileValidationService(_profiles, _registry, _unitOfWork, _clock);
        var profile = SeedProfile(ProviderKind.OpenCodeGo, "Ox Alpha Free");

        var result = await service.ValidateAsync(profile.Id, CancellationToken.None);

        Assert.Equal(ProviderErrorCategory.None, result.Error.Category);
        var invocation = Assert.Single(adapter.Invocations);
        Assert.Equal(profile, invocation.Profile);
        Assert.Equal("SHARPAGENT_OPENCODE_GO_API_KEY", invocation.Secret.EnvironmentVariableName);
    }

    [Fact]
    public async Task Missing_profile_is_a_not_found_error()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.ValidateAsync("missing-profile", CancellationToken.None));
    }

    [Theory]
    [InlineData(ProviderKind.DeepSeek, "SHARPAGENT_DEEPSEEK_API_KEY")]
    [InlineData(ProviderKind.OpenRouter, "SHARPAGENT_OPENROUTER_API_KEY")]
    public async Task Default_secret_references_follow_the_provider(ProviderKind provider, string expectedVariable)
    {
        var adapter = new FakeProviderAdapter(provider);
        var registry = new FakeProviderAdapterRegistry(adapter);
        var service = new ProfileValidationService(_profiles, registry, _unitOfWork, _clock);
        var profile = SeedProfile(provider, $"{provider} Model");

        await service.ValidateAsync(profile.Id, CancellationToken.None);

        Assert.Equal(expectedVariable, Assert.Single(adapter.Invocations).Secret.EnvironmentVariableName);
    }

    [Fact]
    public async Task Operator_config_references_are_honored()
    {
        var withReference = ModelProfile.Register(
            ProviderKind.Fake, "Custom", "id", EndpointKind.ChatCompletions, Now,
            configReference: "MY_CUSTOM_SECRET_VARIABLE");
        _profiles.Seed(withReference);

        await _service.ValidateAsync(withReference.Id, CancellationToken.None);

        Assert.Equal("MY_CUSTOM_SECRET_VARIABLE", Assert.Single(_adapter.Invocations).Secret.EnvironmentVariableName);
        Assert.Equal(ValidationStatus.Validated, withReference.ValidationStatus);
    }

    [Fact]
    public async Task Transport_failures_are_persisted_as_unavailable()
    {
        var throwing = new FakeProviderAdapter(handler: (_, _) => throw new HttpRequestException("boom"));
        var registry = new FakeProviderAdapterRegistry(throwing);
        var service = new ProfileValidationService(_profiles, registry, _unitOfWork, _clock);
        var profile = SeedProfile();

        var result = await service.ValidateAsync(profile.Id, CancellationToken.None);

        Assert.Equal(ProviderErrorCategory.Unavailable, result.Error.Category);
        Assert.DoesNotContain("boom", result.Error.SafeMessage, StringComparison.Ordinal);
        Assert.Equal(ValidationStatus.Failed, profile.ValidationStatus);
    }

    [Fact]
    public async Task Provider_without_a_registered_adapter_fails_safely()
    {
        var registry = new FakeProviderAdapterRegistry(); // no adapters
        var service = new ProfileValidationService(_profiles, registry, _unitOfWork, _clock);
        var profile = SeedProfile();

        await Assert.ThrowsAsync<ConflictException>(
            () => service.ValidateAsync(profile.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_propagates_without_side_effects()
    {
        var profile = SeedProfile();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ValidateAsync(profile.Id, cts.Token));

        Assert.Equal(ValidationStatus.Unvalidated, profile.ValidationStatus);
    }
}

/// <summary>FR-051/FR-055: browser-facing projections never carry credentials or endpoints.</summary>
public sealed class ModelProfileDtoSafetyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Projection_contains_only_safe_selector_fields()
    {
        var profile = ModelProfile.Register(
            ProviderKind.DeepSeek, "DeepSeek Coder", "deepseek-chat", EndpointKind.ChatCompletions, Now,
            configReference: "SHARPAGENT_DEEPSEEK_API_KEY");
        profile.Enable(Now);
        profile.MarkValidated(new ProfileCapabilities(true, true, 64_000, 0.5m, 1.5m), "ok", Now);

        var dto = CatalogService.Project(profile);

        Assert.Equal(profile.Id, dto.Id);
        Assert.Equal("DeepSeek", dto.Provider);
        Assert.Equal("DeepSeek Coder", dto.DisplayName);
        Assert.True(dto.Enabled);
        Assert.True(dto.Streaming);
        Assert.True(dto.ToolCalling);
        Assert.True(dto.EligibleForPlan);
        Assert.True(dto.EligibleForExecute);

        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("SHARPAGENT_DEEPSEEK_API_KEY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("deepseek-chat", json, StringComparison.Ordinal);
        Assert.DoesNotContain("api.", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("key", json, StringComparison.OrdinalIgnoreCase);
    }
}
