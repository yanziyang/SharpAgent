using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpAgent.Application.Health;
using SharpAgent.Infrastructure.Composition;
using SharpAgent.Infrastructure.Persistence;

namespace SharpAgent.Api.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSharpAgentServices(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddInfrastructureServices(configuration);
        services.AddSingleton<HealthQueryService>();

        return services;
    }
}
