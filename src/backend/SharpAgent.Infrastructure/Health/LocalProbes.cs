using SharpAgent.Application.Health;

namespace SharpAgent.Infrastructure.Health;

internal sealed class ApplicationHostProbe : IHealthProbe
{
    public string Name => HealthQueryService.ApplicationProbeName;

    public Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new HealthCheckResult(Name, HealthStatus.Healthy, "API host is running."));
}

/// <summary>Placeholder probe for dependencies that arrive in later phases.</summary>
internal sealed class PendingDependencyProbe(string name, string detail) : IHealthProbe
{
    public string Name { get; } = name;

    public Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new HealthCheckResult(Name, HealthStatus.Degraded, detail));
}
