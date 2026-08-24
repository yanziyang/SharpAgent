using SharpAgent.Application.Common;
using SharpAgent.Application.Profiles;
using SharpAgent.Application.Workspaces;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;
using SharpAgent.TestKit.Fakes;
using Xunit;

namespace SharpAgent.Application.Tests.Catalog;

public sealed class CatalogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Model_profiles_are_projected_without_provider_secrets()
    {
        var profiles = new MemoryModelProfileRepository();
        profiles.Seed(ModelProfile.Register(
            ProviderKind.OpenCodeGo, "Zulu", "z-id", EndpointKind.ChatCompletions, Now));
        var alpha = ModelProfile.Register(
            ProviderKind.Fake, "Alpha Planner", "a-id", EndpointKind.None, Now);
        alpha.SetCapabilities(new ProfileCapabilities(true, true, 8_000, 0.1m, 0.2m), Now);
        alpha.MarkValidated(alpha.GetCapabilities(), "ok", Now.AddMinutes(1));
        alpha.Enable(Now.AddMinutes(1));
        profiles.Seed(alpha);

        var service = new CatalogService(profiles, new MemoryPolicyProfileRepository());

        var dtos = await service.ListModelProfilesAsync();

        Assert.Equal(["Alpha Planner", "Zulu"], dtos.Select(static dto => dto.DisplayName));
        var projected = Assert.Single(dtos, static dto => dto.DisplayName == "Alpha Planner");
        Assert.True(projected.Enabled);
        Assert.Equal("Validated", projected.ValidationStatus);
        Assert.True(projected.EligibleForPlan);
        Assert.True(projected.EligibleForExecute);
        Assert.Equal(8_000, projected.ContextWindowTokens);
    }

    [Fact]
    public async Task Policy_profiles_project_the_operator_limits()
    {
        var policies = new MemoryPolicyProfileRepository();
        policies.Seed(PolicyProfile.Define("default-controlled", 45, 40, 5.00m, 10, Now));
        policies.Seed(PolicyProfile.Define("quick-plan", 10, 12, 1.00m, 5, Now));

        var service = new CatalogService(new MemoryModelProfileRepository(), policies);

        var dtos = await service.ListPolicyProfilesAsync();

        Assert.Equal(["default-controlled", "quick-plan"], dtos.Select(static dto => dto.Name));
        var quick = Assert.Single(dtos, static dto => dto.Name == "quick-plan");
        Assert.Equal(10, quick.MaxRunDurationMinutes);
        Assert.Equal(1.00m, quick.MaxEstimatedCostUsd);
    }
}

public sealed class WorkspaceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 17, 0, 0, TimeSpan.Zero);

    private sealed class Fixture
    {
        public MemoryWorkspaceRepository Workspaces { get; } = new();
        public StubRootValidator Validator { get; set; } = StubRootValidator.ValidFor(@"C:\work\demo");
        public MemoryIdempotencyStore Idempotency { get; } = new();
        public PassThroughUnitOfWork UnitOfWork { get; } = new();
        public FakeClock Clock { get; } = FakeClock.At(2026, 8, 23, 17);

        public WorkspaceService BuildService() => new(Workspaces, Validator, Idempotency, UnitOfWork, Clock);
    }

    private static RegisterWorkspaceRequest Request =>
        new("Demo", @"C:\work\demo");

    [Fact]
    public async Task Registration_validates_and_persists_an_available_workspace()
    {
        var fixture = new Fixture();

        var dto = await fixture.BuildService().RegisterAsync(Request, "ws-key");

        Assert.Equal("Available", dto.Status);
        Assert.Null(dto.ValidationMessage);
        var stored = Assert.Single(fixture.Workspaces.Snapshot);
        Assert.Equal(dto.Id, stored.Id);
        Assert.Equal(@"C:\work\demo", stored.CanonicalRootPath);
    }

    [Fact]
    public async Task Missing_roots_cannot_be_saved()
    {
        var fixture = new Fixture
        {
            Validator = StubRootValidator.Invalid("Root directory does not exist."),
        };

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => fixture.BuildService().RegisterAsync(Request, "ws-key"));

        Assert.True(exception.Errors.ContainsKey("rootPath"));
        Assert.Empty(fixture.Workspaces.Snapshot);
    }

    [Fact]
    public async Task Blank_names_or_paths_are_rejected_before_validation()
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<ValidationException>(
            () => fixture.BuildService().RegisterAsync(Request with { Name = " " }, "k1"));
        await Assert.ThrowsAsync<ValidationException>(
            () => fixture.BuildService().RegisterAsync(Request with { RootPath = string.Empty }, "k2"));
    }

    [Fact]
    public async Task Registration_is_idempotent_per_key()
    {
        var fixture = new Fixture();

        var first = await fixture.BuildService().RegisterAsync(Request, "same");
        var second = await fixture.BuildService().RegisterAsync(Request, "same");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(fixture.Workspaces.Snapshot);
    }

    [Fact]
    public async Task Unvalidated_registration_records_unavailable_state()
    {
        var fixture = new Fixture();

        var dto = await fixture.BuildService().RegisterUnvalidatedAsync(Request, "ws-unvalidated");

        Assert.Equal("Unavailable", dto.Status);
        Assert.NotNull(dto.ValidationMessage);
    }

    [Fact]
    public async Task Listing_orders_workspaces_by_name()
    {
        var fixture = new Fixture();
        await fixture.BuildService().RegisterUnvalidatedAsync(new RegisterWorkspaceRequest("Zeta", @"C:\z"), "z");
        await fixture.BuildService().RegisterUnvalidatedAsync(new RegisterWorkspaceRequest("Alpha", @"C:\a"), "a");

        var list = await fixture.BuildService().ListAsync();

        Assert.Equal(["Alpha", "Zeta"], list.Select(static dto => dto.Name));
    }
}


public sealed class PolicyOverrideDefaultsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cost_override_defaults_to_policy_when_omitted()
    {
        var policy = PolicyProfile.Define("p", 10, 10, 2.00m, 5, Now);

        Assert.Equal(2.00m, policy.ApplyCostOverride(null));
    }
}
