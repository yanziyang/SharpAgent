using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace SharpAgent.LiveProviderTests;

/// <summary>
/// Gates live provider tests behind explicit local opt-in:
/// <c>RUN_LIVE_PROVIDER_TESTS=1</c> and <c>SHARPAGENT_OPENCODE_GO_API_KEY</c>.
/// The key is read from the process environment only; committed code never reads
/// <c>LLM-Key.md</c>. When not opted in, tests are skipped with an explicit reason.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveProviderFactAttribute : FactAttribute
{
    public const string OptInFlagVariable = "RUN_LIVE_PROVIDER_TESTS";
    public const string ApiKeyVariable = "SHARPAGENT_OPENCODE_GO_API_KEY";

    public LiveProviderFactAttribute()
    {
        if (!LiveOptin.TryGetSkipReason(out var reason))
        {
            Skip = reason;
        }
    }
}

public static class LiveOptin
{
    /// <summary>Returns true when live tests may run; otherwise a safe skip reason.</summary>
    public static bool TryGetSkipReason([NotNullWhen(false)] out string? reason)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(LiveProviderFactAttribute.OptInFlagVariable),
                "1",
                StringComparison.Ordinal))
        {
            reason = $"Live provider tests require {LiveProviderFactAttribute.OptInFlagVariable}=1.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LiveProviderFactAttribute.ApiKeyVariable)))
        {
            reason =
                $"Live provider tests require {LiveProviderFactAttribute.ApiKeyVariable} "
                + "to be set from an authorized local source.";
            return false;
        }

        reason = null;
        return true;
    }
}
