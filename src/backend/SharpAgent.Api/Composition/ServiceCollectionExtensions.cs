using SharpAgent.Application.Health;
using SharpAgent.Infrastructure.Composition;

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
