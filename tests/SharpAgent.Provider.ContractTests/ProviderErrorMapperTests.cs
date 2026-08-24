using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Security;
using SharpAgent.Providers.Common;
using Xunit;

namespace SharpAgent.Provider.ContractTests;

/// <summary>
/// Error mapping contract: HTTP statuses and provider error bodies become safe,
/// bounded, canonical errors (FR-054, FR-055). Raw payloads never escape.
/// </summary>
public sealed class ProviderErrorMapperTests
{
    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void Authentication_statuses_map_to_authentication(int statusCode)
    {
        var error = ProviderErrorMapper.Map(statusCode, "{\"error\":{\"message\":\"Invalid API key\"}}");

        Assert.Equal(ProviderErrorCategory.Authentication, error.Category);
        Assert.DoesNotContain("Invalid API key", error.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Rate_limit_maps_to_rate_limited()
    {
        var error = ProviderErrorMapper.Map(429, "{\"error\":{\"message\":\"Too many requests\"}}");

        Assert.Equal(ProviderErrorCategory.RateLimited, error.Category);
    }

    [Fact]
    public void Server_errors_map_to_unavailable()
    {
        var error = ProviderErrorMapper.Map(503, """{"error":{"message":"upstream down"}}""");

        Assert.Equal(ProviderErrorCategory.Unavailable, error.Category);
    }

    [Fact]
    public void Timeout_status_maps_to_timeout()
    {
        var error = ProviderErrorMapper.Map(408, null);

        Assert.Equal(ProviderErrorCategory.Timeout, error.Category);
    }

    [Fact]
    public void Invalid_requests_carry_a_redacted_bounded_message()
    {
        var error = ProviderErrorMapper.Map(400, """{"error":{"message":"model 'sk-secret-key-abcdef1234567890' not found"}}""");

        Assert.Equal(ProviderErrorCategory.InvalidRequest, error.Category);
        Assert.Contains(SecretRedactor.Mask, error.SafeMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-secret-key", error.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Root_message_and_fallback_extraction()
    {
        Assert.Contains("model missing", ProviderErrorMapper.ExtractMessage(
            """{"message":"model missing"}""", "fallback"), StringComparison.Ordinal);
        Assert.Equal("fallback", ProviderErrorMapper.ExtractMessage("not json at all", "fallback"));
        Assert.Equal("fallback", ProviderErrorMapper.ExtractMessage(null, "fallback"));
    }

    [Fact]
    public void Error_messages_are_truncated_to_a_safe_bound()
    {
        var longBody = new string('x', 10_000);
        var error = ProviderErrorMapper.Map(400, "{\"error\":{\"message\":\"" + longBody + "\"}}");

        Assert.True(error.SafeMessage.Length <= ProviderErrorMapper.MaxErrorMessageLength);
    }

    [Fact]
    public void Unknown_statuses_map_to_other()
    {
        var error = ProviderErrorMapper.Map(418, null);

        Assert.Equal(ProviderErrorCategory.Other, error.Category);
    }

    [Fact]
    public void Successful_statuses_map_to_no_error()
    {
        var error = ProviderErrorMapper.Map(200, null);

        Assert.Equal(ProviderErrorCategory.None, error.Category);
    }

    [Fact]
    public void Error_objects_without_messages_fall_back_to_the_root_message()
    {
        var message = ProviderErrorMapper.ExtractMessage(
            """{"error":{"code":42},"message":"root msg"}""", "fallback");

        Assert.Equal("root msg", message);
    }

    [Fact]
    public void Unmatched_payloads_fall_back_safely()
    {
        Assert.Equal("fallback", ProviderErrorMapper.ExtractMessage("""{"other":1}""", "fallback"));
    }
}
