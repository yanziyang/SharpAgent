namespace SharpAgent.Application.Health;

/// <summary>
/// Immutable health projection. Dates are UTC ISO-8601 when serialized;
/// details are operator-authored safe strings only.
/// </summary>
public sealed record HealthSnapshot(
    HealthStatus Overall,
    IReadOnlyList<HealthCheckResult> Checks,
    DateTimeOffset GeneratedAtUtc);
