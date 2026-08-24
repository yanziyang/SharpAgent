namespace SharpAgent.Domain.Profiles;

/// <summary>Registered provider adapters. Fake exists for deterministic offline runs only.</summary>
public enum ProviderKind
{
    OpenCodeGo = 0,
    DeepSeek = 1,
    OpenRouter = 2,

    /// <summary>In-process deterministic provider used by the fake runtime and tests.</summary>
    Fake = 3,
}

public enum EndpointKind
{
    None = 0,
    ChatCompletions = 1,
    Responses = 2,
    AnthropicMessages = 3,
}

public enum ValidationStatus
{
    Unvalidated = 0,
    Validated = 1,
    Failed = 2,
}

/// <summary>Capability document captured during profile validation (FR-052).</summary>
public sealed record ProfileCapabilities(
    bool Streaming,
    bool ToolCalling,
    int? ContextWindowTokens,
    decimal? EstimatedUsdPerMillionInputTokens,
    decimal? EstimatedUsdPerMillionOutputTokens)
{
    public static ProfileCapabilities None { get; } = new(false, false, null, null, null);
}

/// <summary>
/// Operator-managed model profile. Secrets are never stored here; ConfigReference names a
/// server-side source (for example an environment variable) without its value (FR-055).
/// </summary>
public sealed class ModelProfile
{
    public string Id { get; init; } = DomainId.NewModelProfileId();

    public ProviderKind Provider { get; init; }

    public string DisplayName { get; internal set; } = string.Empty;

    /// <summary>Provider-side model identifier, resolved via authorized configuration only.</summary>
    public string ProviderModelId { get; internal set; } = string.Empty;

    public EndpointKind EndpointKind { get; init; }

    /// <summary>JSON-serialized <see cref="ProfileCapabilities"/>.</summary>
    public string CapabilitiesJson { get; internal set; } = "{}";

    public bool Enabled { get; internal set; }

    public ValidationStatus ValidationStatus { get; internal set; }

    /// <summary>Non-secret reference to where credentials live (never the credential).</summary>
    public string? ConfigReference { get; internal set; }

    /// <summary>Safe validation detail shown to operators.</summary>
    public string? ValidationMessage { get; internal set; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; internal set; }

    private ModelProfile()
    {
    }

    public static ModelProfile Register(
        ProviderKind provider,
        string displayName,
        string providerModelId,
        EndpointKind endpointKind,
        DateTimeOffset nowUtc,
        string? configReference = null)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(providerModelId))
        {
            throw new ArgumentException("Provider model id is required.", nameof(providerModelId));
        }

        return new ModelProfile
        {
            Provider = provider,
            DisplayName = displayName,
            ProviderModelId = providerModelId,
            EndpointKind = endpointKind,
            ConfigReference = configReference,
            Enabled = false,
            ValidationStatus = ValidationStatus.Unvalidated,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void SetCapabilities(ProfileCapabilities capabilities, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        CapabilitiesJson = System.Text.Json.JsonSerializer.Serialize(capabilities);
        UpdatedAtUtc = nowUtc;
    }

    public ProfileCapabilities GetCapabilities() =>
        System.Text.Json.JsonSerializer.Deserialize<ProfileCapabilities>(CapabilitiesJson) ?? ProfileCapabilities.None;

    public void Enable(DateTimeOffset nowUtc)
    {
        Enabled = true;
        UpdatedAtUtc = nowUtc;
    }

    public void Disable(DateTimeOffset nowUtc)
    {
        Enabled = false;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Execute mode requires streaming plus tool calling on a validated profile (FR-052).</summary>
    public bool CanExecute() =>
        Enabled && ValidationStatus == ValidationStatus.Validated
        && GetCapabilities() is { Streaming: true, ToolCalling: true };

    /// <summary>
    /// Enabled profiles may plan; only failed validation blocks planning outright.
    /// Unvalidated profiles stay plan-only until validation declares capabilities (E2E-08).
    /// </summary>
    public bool CanPlan() => Enabled && ValidationStatus != ValidationStatus.Failed;

    public void MarkValidated(ProfileCapabilities capabilities, string? safeMessage, DateTimeOffset nowUtc)
    {
        ValidationStatus = ValidationStatus.Validated;
        ValidationMessage = safeMessage;
        SetCapabilities(capabilities, nowUtc);
        UpdatedAtUtc = nowUtc;
    }

    public void MarkValidationFailed(string safeMessage, DateTimeOffset nowUtc)
    {
        ValidationStatus = ValidationStatus.Failed;
        ValidationMessage = safeMessage;
        UpdatedAtUtc = nowUtc;
    }
}
