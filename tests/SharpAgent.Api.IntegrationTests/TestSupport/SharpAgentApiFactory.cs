using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpAgent.Application.Health;

namespace SharpAgent.Api.IntegrationTests.TestSupport;

/// <summary>
/// Boots the real API composition root. Tests may replace all health probes with fakes
/// to drive healthy/degraded/unready scenarios deterministically.
/// </summary>
public sealed class SharpAgentApiFactory : WebApplicationFactory<Program>
{
    public IReadOnlyList<IHealthProbe>? ProbeOverrides { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
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
