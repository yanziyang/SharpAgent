using Microsoft.Extensions.DependencyInjection;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Dashboard;
using SharpAgent.Application.Idempotency;
using SharpAgent.Application.Profiles;
using SharpAgent.Application.Observability;
using SharpAgent.Application.Runs;
using SharpAgent.Application.Sessions;
using SharpAgent.Application.Workspaces;

namespace SharpAgent.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<SessionService>();
        services.AddScoped<WorkspaceService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<DashboardQueryService>();
        services.AddScoped<ObservabilityQueryService>();
        services.AddScoped<ProfileValidationService>();
        services.AddScoped<RunOrchestrator>();
        services.AddScoped<ICorrelationContext, CorrelationContext>();
        services.AddSingleton<ISessionEventPublisher, InMemorySessionEventPublisher>();

        // IdempotencyService is constructed by consumers with their store; expose factory defaults.
        services.AddSingleton(sp => new IdempotencyOptions());

        return services;
    }
}

/// <summary>Documented idempotency retention configuration.</summary>
public sealed class IdempotencyOptions
{
    public TimeSpan Retention { get; set; } = IdempotencyService.DefaultRetention;
}
