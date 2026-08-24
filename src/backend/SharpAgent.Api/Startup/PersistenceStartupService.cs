using SharpAgent.Application.Abstractions;
using SharpAgent.Infrastructure.Persistence;

namespace SharpAgent.Api.Startup;

/// <summary>Applies migrations and recovery sweeps once, before the app serves traffic.</summary>
public sealed class PersistenceStartupService(DbInitializer initializer) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
