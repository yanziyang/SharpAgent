using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpAgent.Application.Health;

namespace SharpAgent.Infrastructure.Composition;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Phase 0 placeholders. Later phases replace these with real EF/SQLite,
        // workspace-executor, and provider-readiness probes.
        services.AddSingleton<IHealthProbe>(new ApplicationHostProbe());
        services.AddSingleton<IHealthProbe>(new PendingDependencyProbe(
            "database",
            "SQLite persistence is not configured yet."));
        services.AddSingleton<IHealthProbe>(new PendingDependencyProbe(
            "workspace-executor",
            "Workspace execution is not configured yet."));
        services.AddSingleton<IHealthProbe>(new PendingDependencyProbe(
            "providers",
            "No provider adapter is registered yet."));

        return services;
    }
}

internal sealed class ApplicationHostProbe : IHealthProbe
{
    public string Name => HealthQueryService.ApplicationProbeName;

    public Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new HealthCheckResult(Name, HealthStatus.Healthy, "API host is running."));
}

internal sealed class PendingDependencyProbe : IHealthProbe
{
    private readonly string _detail;

    public PendingDependencyProbe(string name, string detail)
    {
        Name = name;
        _detail = detail;
    }

    public string Name { get; }

    public Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new HealthCheckResult(Name, HealthStatus.Degraded, _detail));
}
