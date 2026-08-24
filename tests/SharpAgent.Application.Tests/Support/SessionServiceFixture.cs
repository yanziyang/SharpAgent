using SharpAgent.Application.Sessions;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Workspaces;
using SharpAgent.TestKit.Fakes;

namespace SharpAgent.Application.Tests.Support;

/// <summary>Assembles SessionService with deterministic in-memory fakes and seeded references.</summary>
public sealed class SessionServiceFixture
{
    public FakeClock Clock { get; } = FakeClock.At(2026, 8, 23, 14);

    public MemorySessionRepository Sessions { get; } = new();

    public MemoryWorkspaceRepository Workspaces { get; } = new();

    public MemoryModelProfileRepository Profiles { get; } = new();

    public MemoryPolicyProfileRepository Policies { get; } = new();

    public MemoryTodoRepository Todos { get; } = new();

    public MemoryAuditEventRepository Events { get; } = new();

    public MemoryRunLeaseRepository Leases { get; } = new();

    public MemoryIdempotencyStore Idempotency { get; } = new();

    public PassThroughUnitOfWork UnitOfWork { get; } = new();

    public SessionService Service { get; }

    public string WorkspaceId { get; }

    public string ModelProfileId { get; }

    public string PolicyProfileId { get; }

    public SessionServiceFixture(bool validatedStreamingToolProfile = true)
    {
        var now = Clock.UtcNow;

        var workspace = Workspace.Register("Demo", @"C:\work\demo", now);
        workspace.MarkValidated(@"C:\work\demo", now);
        Workspaces.AddAsync(workspace, CancellationToken.None).Wait();
        WorkspaceId = workspace.Id;

        var profile = ModelProfile.Register(
            ProviderKind.Fake,
            "Fake Planner",
            "fake-planner-v1",
            EndpointKind.None,
            now,
            configReference: null);
        profile.SetCapabilities(
            new ProfileCapabilities(
                Streaming: true,
                ToolCalling: true,
                ContextWindowTokens: 64_000,
                EstimatedUsdPerMillionInputTokens: 0m,
                EstimatedUsdPerMillionOutputTokens: 0m),
            now);

        if (validatedStreamingToolProfile)
        {
            profile.MarkValidated(profile.GetCapabilities(), "ok", now);
        }

        profile.Enable(now);
        Profiles.Seed(profile);
        ModelProfileId = profile.Id;

        var policy = PolicyProfile.Define("default-controlled", 45, 40, 5.00m, 10, now);
        Policies.Seed(policy);
        PolicyProfileId = policy.Id;

        Service = new SessionService(
            Sessions,
            Workspaces,
            Profiles,
            Policies,
            Todos,
            Events,
            Leases,
            Idempotency,
            UnitOfWork,
            Clock);
    }
}
