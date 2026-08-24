using SharpAgent.Application.Tests.Support;
using SharpAgent.TestKit.Fakes;
using SharpAgent.Application.Common;
using SharpAgent.Application.Sessions;
using SharpAgent.Domain.Sessions;
using Xunit;

namespace SharpAgent.Application.Tests.Sessions;

public sealed class CreateRequestEdgeCasesTests
{
    [Fact]
    public async Task Undefined_mode_values_are_rejected()
    {
        var fixture = new SessionServiceFixture();

        var request = new CreateSessionRequest(
            fixture.WorkspaceId,
            "task",
            (SessionMode)99,
            fixture.ModelProfileId,
            fixture.PolicyProfileId);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => fixture.Service.CreateAsync(request, "k-mode"));

        Assert.True(exception.Errors.ContainsKey("mode"));
    }

    [Fact]
    public async Task Disabled_profiles_block_plan_mode_too()
    {
        var fixture = new SessionServiceFixture(validatedStreamingToolProfile: true);
        fixture.Profiles.Snapshot.Single().Disable(fixture.Clock.UtcNow);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.CreateAsync(
                new CreateSessionRequest(
                    fixture.WorkspaceId, "task", SessionMode.Plan,
                    fixture.ModelProfileId, fixture.PolicyProfileId),
                "k-disabled"));

        Assert.True(exception.Errors.TryGetValue("modelProfileId", out var messages));
        Assert.Contains("enabled", string.Join(" ", messages!), StringComparison.OrdinalIgnoreCase);
    }
}

