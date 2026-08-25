using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;
using SharpAgent.Infrastructure.Persistence;

namespace SharpAgent.Infrastructure.Setup;

/// <summary>Explicit opt-in switch for the credential-free Development walkthrough.</summary>
public sealed record LocalDemoOptions(bool Enabled)
{
    public const string EnabledKey = "LocalDemo:Enabled";

    public static LocalDemoOptions FromConfiguration(IConfiguration configuration) =>
        new(bool.TryParse(configuration[EnabledKey], out var enabled) && enabled);
}

/// <summary>
/// Seeds only non-secret Development catalog records. It never creates a
/// workspace, reads credentials, or calls a provider.
/// </summary>
public sealed class LocalDemoCatalogSeeder(
    IDbContextFactory<SharpAgentDbContext> contextFactory,
    LocalDemoOptions options)
{
    private const string DemoProfileName = "Offline demo (Plan only)";
    private const string DefaultPolicyName = "Default safe policy";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            return;
        }

        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var nowUtc = DateTimeOffset.UtcNow;
        var changed = false;

        if (!await context.ModelProfiles.AnyAsync(
                profile => profile.DisplayName == DemoProfileName,
                cancellationToken).ConfigureAwait(false))
        {
            var profile = ModelProfile.Register(
                ProviderKind.Fake,
                DemoProfileName,
                "sharpagent-local-demo",
                EndpointKind.None,
                nowUtc,
                configReference: "local-demo");
            profile.MarkValidated(
                new ProfileCapabilities(
                    Streaming: true,
                    ToolCalling: false,
                    ContextWindowTokens: 16_000,
                    EstimatedUsdPerMillionInputTokens: null,
                    EstimatedUsdPerMillionOutputTokens: null),
                "Deterministic local walkthrough; no external provider request.",
                nowUtc);
            profile.Enable(nowUtc);
            await context.ModelProfiles.AddAsync(profile, cancellationToken).ConfigureAwait(false);
            changed = true;
        }

        if (!await context.PolicyProfiles.AnyAsync(
                policy => policy.Name == DefaultPolicyName,
                cancellationToken).ConfigureAwait(false))
        {
            await context.PolicyProfiles.AddAsync(
                PolicyProfile.Define(
                    DefaultPolicyName,
                    maxRunDurationMinutes: 15,
                    maxToolCalls: 20,
                    maxEstimatedCostUsd: 1m,
                    approvalExpiryMinutes: 10,
                    nowUtc),
                cancellationToken).ConfigureAwait(false);
            changed = true;
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>Runs the local catalog seed after migrations and recovery complete.</summary>
public sealed class LocalDemoCatalogStartupService(LocalDemoCatalogSeeder seeder) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => seeder.SeedAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
