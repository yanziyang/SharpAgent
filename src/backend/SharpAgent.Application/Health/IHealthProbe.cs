namespace SharpAgent.Application.Health;

/// <summary>
/// Probe for one application dependency (application, database, executor, provider).
/// Implementations live at the infrastructure edge and must never throw sensitive data;
/// failures are converted to bounded degraded results by <see cref="HealthQueryService"/>.
/// </summary>
public interface IHealthProbe
{
    string Name { get; }

    Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken);
}
