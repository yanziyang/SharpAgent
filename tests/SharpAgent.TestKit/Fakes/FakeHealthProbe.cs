using SharpAgent.Application.Health;

namespace SharpAgent.TestKit.Fakes;

/// <summary>
/// Deterministic fake health probe for tests. Supports fixed results, call counting,
/// and fault injection without any infrastructure dependency.
/// </summary>
public sealed class FakeHealthProbe : IHealthProbe
{
    private readonly Func<CancellationToken, Task<HealthCheckResult>> _handler;

    public FakeHealthProbe(string name, HealthStatus status = HealthStatus.Healthy, string? detail = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Probe name is required.", nameof(name));
        }

        Name = name;
        _handler = _ => Task.FromResult(new HealthCheckResult(name, status, detail));
    }

    /// <summary>Fault/async injection: the handler receives no arguments and may throw.</summary>
    public FakeHealthProbe(string name, Func<Task<HealthCheckResult>> handler)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Probe name is required.", nameof(name));
        }

        Name = name;
        _handler = _ => handler();
    }

    public string Name { get; }

    public int CallCount { get; private set; }

    public async Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        return await _handler(cancellationToken).ConfigureAwait(false);
    }
}
