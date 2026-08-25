using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpAgent.Application.Health;
using SharpAgent.Infrastructure.Persistence;
using SharpAgent.Infrastructure.Setup;

namespace SharpAgent.Api.IntegrationTests.TestSupport;

/// <summary>
/// Boots the real composition root against a fresh SQLite file when SqlitePath is set;
/// migrations and recovery sweeps run through the hosted startup service.
/// </summary>
public sealed class SharpAgentApiFactory : WebApplicationFactory<Program>
{
    public string? SqlitePath { get; init; }

    public IReadOnlyList<IHealthProbe>? ProbeOverrides { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting(LocalDemoOptions.EnabledKey, "false");

        if (!string.IsNullOrWhiteSpace(SqlitePath))
        {
            builder.UseSetting(InfrastructureOptions.SqlitePathKey, SqlitePath);
        }
        builder.ConfigureServices(services =>
        {
            if (ProbeOverrides is { Count: > 0 } overrides)
            {
                services.RemoveAll<IHealthProbe>();
                foreach (var probe in overrides)
                {
                    services.AddSingleton(probe);
                }
            }
        });
    }
}
