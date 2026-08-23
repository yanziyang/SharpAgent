using Xunit;

namespace SharpAgent.LiveProviderTests;

public sealed class LiveOptinGuardTests
{
    private const string Flag = LiveProviderFactAttribute.OptInFlagVariable;
    private const string Key = LiveProviderFactAttribute.ApiKeyVariable;

    [Fact]
    public void Skips_when_optin_flag_is_missing()
    {
        RunWithEnvironment(flag: null, key: "unused", () =>
        {
            var optedIn = LiveOptin.TryGetSkipReason(out var reason);

            Assert.False(optedIn);
            Assert.Contains(Flag, reason);
        });
    }

    [Fact]
    public void Skips_when_api_key_is_missing()
    {
        RunWithEnvironment(flag: "1", key: null, () =>
        {
            var optedIn = LiveOptin.TryGetSkipReason(out var reason);

            Assert.False(optedIn);
            Assert.Contains(Key, reason);
        });
    }

    [Fact]
    public void Optin_requires_both_variables()
    {
        RunWithEnvironment(flag: "1", key: "local-test-key-value", () =>
        {
            var optedIn = LiveOptin.TryGetSkipReason(out _);

            Assert.True(optedIn);
        });
    }

    // Real OpenCode Go Plan smoke validation arrives with the provider adapter phase
    // (Implementation Plan sections 4.2-4.3). Its allowlist is exactly:
    // Ox Alpha Free, Muse Spark 1.2 Contributor, MiMo-V2.5. Every such test will be
    // marked [LiveProviderFact] so it skips without explicit local opt-in.

    private static void RunWithEnvironment(string? flag, string? key, Action action)
    {
        var originalFlag = Environment.GetEnvironmentVariable(Flag);
        var originalKey = Environment.GetEnvironmentVariable(Key);

        try
        {
            if (flag is null)
            {
                Environment.SetEnvironmentVariable(Flag, null);
            }
            else
            {
                Environment.SetEnvironmentVariable(Flag, flag);
            }

            if (key is null)
            {
                Environment.SetEnvironmentVariable(Key, null);
            }
            else
            {
                Environment.SetEnvironmentVariable(Key, key);
            }

            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Flag, originalFlag);
            Environment.SetEnvironmentVariable(Key, originalKey);
        }
    }
}
