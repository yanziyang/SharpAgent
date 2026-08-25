using Microsoft.EntityFrameworkCore;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Health;

namespace SharpAgent.Infrastructure.Health;

internal sealed class ApplicationHostProbe : IHealthProbe
{
    public string Name => HealthQueryService.ApplicationProbeName;

    public Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new HealthCheckResult(Name, HealthStatus.Healthy, "API host is running."));
}

/// <summary>Readiness of the server-side workspace execution boundary.</summary>
internal sealed class WorkspaceExecutorProbe(
    IWorkspaceRootValidator rootValidator,
    IProcessRunner processRunner) : IHealthProbe
{
    public string Name => "workspace-executor";

    public Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new HealthCheckResult(
            Name,
            rootValidator is not null && processRunner is not null
                ? HealthStatus.Healthy
                : HealthStatus.Unready,
            rootValidator is not null && processRunner is not null
                ? "Workspace executor is available."
                : "Workspace executor is unavailable."));
}

/// <summary>Checks enabled model profiles against registered server-side adapters.</summary>
internal sealed class ProviderReadinessProbe(
    IDbContextFactory<Persistence.SharpAgentDbContext> contextFactory,
    IProviderAdapterRegistry adapters) : IHealthProbe
{
    public string Name => "providers";

    public async Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var enabledProviders = await context.ModelProfiles
            .AsNoTracking()
            .Where(static profile => profile.Enabled)
            .Select(static profile => profile.Provider)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (enabledProviders.Count == 0)
        {
            return new HealthCheckResult(Name, HealthStatus.Degraded, "No enabled provider profile is configured.");
        }

        if (enabledProviders.Any(provider => adapters.Find(provider) is null))
        {
            return new HealthCheckResult(Name, HealthStatus.Degraded, "An enabled provider has no server adapter.");
        }

        return new HealthCheckResult(Name, HealthStatus.Healthy, "Enabled provider adapters are ready.");
    }
}
