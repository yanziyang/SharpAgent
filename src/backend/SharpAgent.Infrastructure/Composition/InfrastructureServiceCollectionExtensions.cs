using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Health;
using SharpAgent.Infrastructure.Health;
using SharpAgent.Infrastructure.Persistence;
using SharpAgent.Infrastructure.Timing;
using SharpAgent.Infrastructure.Workspaces;

namespace SharpAgent.Infrastructure.Composition;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);

        var sqliteDataSource = DatabasePath.Resolve(
            configuration[InfrastructureOptions.SqlitePathKey]
            ?? DesignTimeDbContextFactory.DefaultDatabasePath);
        var sqliteConnectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = sqliteDataSource,
        }.ToString();

        services.AddSingleton<SqlitePragmaInterceptor>();
        services.AddDbContextFactory<SharpAgentDbContext>(
            (serviceProvider, options) => options
                .UseSqlite(sqliteConnectionString)
                .AddInterceptors(serviceProvider.GetRequiredService<SqlitePragmaInterceptor>()));
        services.AddScoped(static serviceProvider => serviceProvider
            .GetRequiredService<IDbContextFactory<SharpAgentDbContext>>()
            .CreateDbContext());

        // Application ports.
        services.AddScoped<ISessionRepository, EfSessionRepository>();
        services.AddScoped<IWorkspaceRepository, EfWorkspaceRepository>();
        services.AddScoped<IModelProfileRepository, EfModelProfileRepository>();
        services.AddScoped<IPolicyProfileRepository, EfPolicyProfileRepository>();
        services.AddScoped<ITodoRepository, EfTodoRepository>();
        services.AddScoped<IAuditEventRepository, EfAuditEventRepository>();
        services.AddScoped<IRunLeaseRepository, EfRunLeaseRepository>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // Singletons/edge services.
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<IWorkspaceRootValidator, FileSystemRootValidator>();
        services.AddSingleton<DbInitializer>();

        // Health probes: real SQLite readiness; other dependencies arrive in later phases.
        services.AddSingleton<IHealthProbe>(new ApplicationHostProbe());
        services.AddSingleton<IHealthProbe>(serviceProvider =>
            new SqliteDatabaseProbe(serviceProvider.GetRequiredService<IDbContextFactory<SharpAgentDbContext>>()));
        services.AddSingleton<IHealthProbe>(new PendingDependencyProbe(
            "workspace-executor",
            "Workspace execution is not configured yet."));
        services.AddSingleton<IHealthProbe>(new PendingDependencyProbe(
            "providers",
            "No provider adapter is registered yet."));

        return services;
    }
}
