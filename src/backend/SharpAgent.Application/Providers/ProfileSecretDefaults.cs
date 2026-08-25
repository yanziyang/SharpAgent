using SharpAgent.Domain.Profiles;

namespace SharpAgent.Application.Providers;

/// <summary>Shared server-side default secret variable names per provider (FR-055).</summary>
public static class ProfileSecretDefaults
{
    public static string VariableFor(ProviderKind provider) => provider switch
    {
        ProviderKind.OpenCodeGo => "SHARPAGENT_OPENCODE_GO_API_KEY",
        ProviderKind.DeepSeek => "SHARPAGENT_DEEPSEEK_API_KEY",
        ProviderKind.OpenRouter => "SHARPAGENT_OPENROUTER_API_KEY",
        ProviderKind.Fake => "SHARPAGENT_FAKE_API_KEY",
        _ => "SHARPAGENT_PROVIDER_API_KEY",
    };
}
