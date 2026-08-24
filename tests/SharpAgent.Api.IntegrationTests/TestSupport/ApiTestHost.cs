using Microsoft.Extensions.DependencyInjection;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Policies;
using SharpAgent.Infrastructure.Persistence;
using SharpAgent.TestKit.Workspaces;

namespace SharpAgent.Api.IntegrationTests.TestSupport;

/// <summary>One disposable factory + seeded catalog per test.</summary>
public sealed class ApiTestHost : IDisposable
{
    private readonly TempWorkspace _workspace;

    private ApiTestHost(SharpAgentApiFactory factory, TempWorkspace workspace)
    {
        Factory = factory;
        _workspace = workspace;
    }

    public SharpAgentApiFactory Factory { get; }

    public HttpClient Client { get; private set; } = null!;

    public (string WorkspaceId, string ModelProfileId, string PolicyProfileId) Seed { get; private set; }

    public static ApiTestHost Start()
    {
        var workspace = TempWorkspace.Create();
        var factory = new SharpAgentApiFactory
        {
            SqlitePath = Path.Combine(workspace.RootPath, "sharpagent-api.db"),
        };
        var host = new ApiTestHost(factory, workspace);
        host.Client = factory.CreateClient(); // starts the host (runs migrations)
        return host;
    }

    /// <summary>Inserts operator configuration rows directly (registration UI is a later phase).</summary>
    public async Task<(string WorkspaceId, string ModelProfileId, string PolicyProfileId)> SeedCatalogAsync(
        bool profileValidatedForExecute = true,
        string? configReference = null)
    {
        if (Seed.WorkspaceId is not null)
        {
            return Seed;
        }
        await using var scopeScope = Factory.Services.CreateAsyncScope();
        var context = scopeScope.ServiceProvider.GetRequiredService<SharpAgentDbContext>();

        var now = DateTimeOffset.UtcNow;

        var workspace = Domain.Workspaces.Workspace.Register("Demo", @"C:\work\demo", now);
        workspace.MarkValidated(@"C:\work\demo", now);
        await context.Workspaces.AddAsync(workspace);

        var profile = Domain.Profiles.ModelProfile.Register(
            ProviderKind.Fake,
            "Fake Planner",
            "fake-planner-v1",
            EndpointKind.None,
            now,
            configReference);
        profile.SetCapabilities(
            new ProfileCapabilities(true, true, 64_000, null, null),
            now);
        if (profileValidatedForExecute)
        {
            profile.MarkValidated(profile.GetCapabilities(), "ok", now);
        }

        profile.Enable(now);
        await context.ModelProfiles.AddAsync(profile);

        var policy = Domain.Policies.PolicyProfile.Define("default-controlled", 45, 40, 5.00m, 10, now);
        await context.PolicyProfiles.AddAsync(policy);

        await context.SaveChangesAsync();

        Seed = (workspace.Id, profile.Id, policy.Id);
        return Seed;
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        _workspace.Dispose();
    }
}
