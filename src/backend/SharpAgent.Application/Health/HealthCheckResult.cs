namespace SharpAgent.Application.Health;

/// <summary>Result of probing a single dependency. <paramref name="Detail"/> is safe for display.</summary>
public sealed record HealthCheckResult(string Name, HealthStatus Status, string? Detail);
