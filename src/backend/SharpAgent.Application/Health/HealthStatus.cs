namespace SharpAgent.Application.Health;

/// <summary>Aggregated or per-dependency readiness reported to the browser.</summary>
public enum HealthStatus
{
    Healthy = 0,
    Degraded = 1,
    Unready = 2,
}
