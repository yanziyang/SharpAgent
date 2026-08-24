using Microsoft.Extensions.DependencyInjection;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Profiles;
using SharpAgent.Providers.Common;

namespace SharpAgent.Providers;

/// <summary>Resolves the registered adapter for a provider kind.</summary>
public sealed class ProviderAdapterRegistry(IEnumerable<IModelProviderAdapter> adapters) : IProviderAdapterRegistry
{
    private readonly Dictionary<ProviderKind, IModelProviderAdapter> _byProvider =
            adapters.ToDictionary(static adapter => adapter.Provider);

    public IModelProviderAdapter? Find(ProviderKind provider) =>
        _byProvider.TryGetValue(provider, out var adapter) ? adapter : null;
}

public static class ProvidersServiceCollectionExtensions
{
    /// <summary>
    /// Registers the provider adapters and the adapter registry. Adapters share one
    /// HttpClient with a short timeout; automatic fallback stays OFF (FR-056).
    /// </summary>
    public static IServiceCollection AddProviderAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(25) });
        services.AddSingleton<ProviderValidationRunner>();
        services.AddSingleton<IModelProviderAdapter, OpenCodeGoAdapter>();
        services.AddSingleton<IModelProviderAdapter, DeepSeekAdapter>();
        services.AddSingleton<IModelProviderAdapter, OpenRouterAdapter>();
        services.AddSingleton<IProviderAdapterRegistry, ProviderAdapterRegistry>();

        return services;
    }
}
