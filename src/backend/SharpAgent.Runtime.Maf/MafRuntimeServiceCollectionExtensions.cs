using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SharpAgent.Application.Abstractions;

namespace SharpAgent.Runtime.Maf;

public static class MafRuntimeServiceCollectionExtensions
{
    /// <summary>Registers the Microsoft Agent Framework runtime adapter.</summary>
    public static IServiceCollection AddMafRuntime(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(AgentToolOptions.FromConfiguration(configuration));
        services.AddSingleton<IAgentRuntime, MafAgentRuntime>();

        return services;
    }
}
