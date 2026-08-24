using System.Text.Json;
using SharpAgent.Application.Profiles;
using SharpAgent.Application.Security;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Workspaces;
using SharpAgent.TestKit.Fakes;
using Xunit;

namespace SharpAgent.Application.Tests.Security;

/// <summary>Secret-boundary proofs for projections (Implementation Plan section 8.2).</summary>
public sealed class ProjectionRedactionTests
{
    private const string Marker = "sk-totallyarealsecretvalue123456"; // sharpagent:fixture-secret

    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string ToJson<T>(T value) where T : notnull => JsonSerializer.Serialize(value, JsonOptions);

    [Fact]
    public void Model_profile_projection_never_includes_the_config_reference()
    {
        var profile = ModelProfile.Register(
            ProviderKind.OpenCodeGo,
            "Ox Alpha Free",
            "provider-side-id",
            EndpointKind.ChatCompletions,
            Now,
            configReference: $"env:KEY raw={Marker}");
        profile.Enable(Now);

        // Control: the entity itself carries the marker, proving the test setup.
        Assert.Contains(Marker, ToJson(profile), StringComparison.Ordinal);

        var dto = CatalogService.Project(profile);
        var json = ToJson(dto);

        Assert.DoesNotContain("configReference", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Marker, json, StringComparison.Ordinal);
        Assert.DoesNotContain("providerModelId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Workspace_validation_messages_are_masked_when_they_carry_secret_shapes()
    {
        var workspace = Workspace.Register("Leaky", @"C:\work\leaky", Now);
        workspace.MarkUnavailable($"Auth failed bearer abcdefghijklmnopqrstuvwxyz123 {Marker}", Now.AddMinutes(1));

        var dto = SharpAgent.Application.Workspaces.WorkspaceService.Project(workspace);

        Assert.Contains(SecretRedactor.Mask, dto.ValidationMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, dto.ValidationMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz123", dto.ValidationMessage, StringComparison.Ordinal);
    }

    private static readonly string[] SessionPropertyNames =
    [
        // Alphabetical: assertions compare against the sorted serialized property names.
        "activeRunId", "archived", "createdAtUtc", "id", "mode", "modelProfileId",
        "policyProfileId", "runs", "status", "task", "updatedAtUtc", "workspaceId",
    ];

    private static readonly string[] RunPropertyNames =
    [
        "endedAtUtc", "id", "resumeSourceRunId", "sequence", "startedAtUtc", "status", "stopReason",
    ];

    [Fact]
    public void Session_projection_exposes_only_contracted_fields()
    {
        var session = Domain.Sessions.Session.CreateNew("ws", "task", SessionMode.Plan, "m", "p", Now);
        session.BeginRun(Now.AddMinutes(1));

        var json = JsonSerializer.Serialize(
            SharpAgent.Application.Sessions.SessionService.Project(session),
            JsonOptions);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            SessionPropertyNames,
            document.RootElement.EnumerateObject().Select(static property => property.Name).Order());

        foreach (var run in document.RootElement.GetProperty("runs").EnumerateArray())
        {
            Assert.Equal(
                RunPropertyNames,
                run.EnumerateObject().Select(static property => property.Name).Order());
        }
    }

    [Theory]
    [InlineData("sk-abcdefghijklmnop1234")] // sharpagent:fixture-secret
    [InlineData("ghp_abcdefghijklmnopqrstuvwx")] // sharpagent:fixture-secret
    [InlineData("AKIAABCDEFGHIJKLMNOP")] // sharpagent:fixture-secret
    [InlineData("xoxb-abcdefghijklmn")] // sharpagent:fixture-secret
    [InlineData("Bearer abcdefghijklmnopqrstuvwxyz0123456789abcd")] // sharpagent:fixture-secret
    public void Redactor_masks_high_confidence_secret_shapes(string secret)
    {
        var masked = SecretRedactor.Redact($"prefix {secret} suffix");

        Assert.DoesNotContain(secret, masked, StringComparison.Ordinal);
        Assert.Contains(SecretRedactor.Mask, masked, StringComparison.Ordinal);
        Assert.StartsWith("prefix ", masked, StringComparison.Ordinal);
        Assert.EndsWith(" suffix", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void Redactor_leaves_ordinary_text_untouched()
    {
        const string normal = "Run the focused pricing tests on Windows.";

        Assert.Equal(normal, SecretRedactor.Redact(normal));
        Assert.Null(SecretRedactor.Redact(null));
    }
}




