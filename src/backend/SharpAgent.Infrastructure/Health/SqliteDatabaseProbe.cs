using Microsoft.EntityFrameworkCore;
using SharpAgent.Application.Health;

namespace SharpAgent.Infrastructure.Health;

/// <summary>Reports SQLite reachability without exposing paths or raw errors.</summary>
internal sealed class SqliteDatabaseProbe(IDbContextFactory<Persistence.SharpAgentDbContext> contextFactory)
    : IHealthProbe
{
    public string Name => "database";

    public async Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            var reachable = await context.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
            return reachable
                ? new HealthCheckResult(Name, HealthStatus.Healthy, "SQLite is reachable.")
                : new HealthCheckResult(Name, HealthStatus.Unready, "SQLite is not reachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Never surface the connection string, path, or raw provider error.
            return new HealthCheckResult(Name, HealthStatus.Degraded, "SQLite check failed.");
        }
    }
}
