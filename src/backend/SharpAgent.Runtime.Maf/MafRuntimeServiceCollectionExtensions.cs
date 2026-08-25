using Microsoft.Extensions.DependencyInjection;
using SharpAgent.Application.Abstractions;

namespace SharpAgent.Runtime.Maf;

public static class MafRuntimeServiceCollectionExtensions
{
    /// <summary>Registers the Microsoft Agent Framework runtime adapter.</summary>
    public static IServiceCollection AddMafRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAgentRuntime, MafAgentRuntime>();

        return services;
    }
}
