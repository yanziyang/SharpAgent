namespace SharpAgent.Application.Health;

/// <summary>
/// Aggregates dependency probes into one safe health snapshot.
/// Probe failures never leak exception data: they become bounded degraded results.
/// </summary>
public sealed class HealthQueryService
{
    public const string ApplicationProbeName = "application";

    private readonly List<IHealthProbe> _probes;

    public HealthQueryService(IEnumerable<IHealthProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);
        _probes = [.. probes.OrderBy(static p => p.Name, StringComparer.Ordinal)];
    }

    public async Task<HealthSnapshot> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<HealthCheckResult>(_probes.Count);

        foreach (var probe in _probes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ProbeSafeAsync(probe, cancellationToken).ConfigureAwait(false));
        }

        return new HealthSnapshot(OverallStatus(results), results, DateTimeOffset.UtcNow);
    }

    private static async Task<HealthCheckResult> ProbeSafeAsync(IHealthProbe probe, CancellationToken cancellationToken)
    {
        HealthCheckResult result;

        try
        {
            result = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            result = new HealthCheckResult(probe.Name, HealthStatus.Degraded, "Probe failed.");
        }

        return Normalize(result, probe.Name);
    }

    private static HealthCheckResult Normalize(HealthCheckResult result, string expectedName)
    {
        var name = string.IsNullOrWhiteSpace(result.Name) ? expectedName : result.Name;
        var status = Enum.IsDefined(result.Status) ? result.Status : HealthStatus.Degraded;
        return result.Name == name && result.Status == status
            ? result
            : new HealthCheckResult(name, status, result.Detail);
    }

    private static HealthStatus OverallStatus(IReadOnlyList<HealthCheckResult> results)
    {
        var overall = HealthStatus.Healthy;

        foreach (var result in results)
        {
            if (result.Status > overall)
            {
                overall = result.Status;
            }
        }

        return overall;
    }
}
