using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Health;
using SharpAgent.Application.Tools;
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
        services.AddScoped<IDashboardQueryRepository, EfDashboardQueryRepository>();
        services.AddScoped<IWorkspaceRepository, EfWorkspaceRepository>();
        services.AddScoped<IModelProfileRepository, EfModelProfileRepository>();
        services.AddScoped<IPolicyProfileRepository, EfPolicyProfileRepository>();
        services.AddScoped<ITodoRepository, EfTodoRepository>();
        services.AddScoped<IApprovalRequestRepository, EfApprovalRequestRepository>();
        services.AddScoped<IChangeSetStore, EfChangeSetStore>();
        services.AddScoped<IToolExecutionRepository, EfToolExecutionRepository>();
        services.AddScoped<IAuditEventRepository, EfAuditEventRepository>();
        services.AddScoped<IRunLeaseRepository, EfRunLeaseRepository>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // Tool execution edges and services.
        services.AddSingleton(FocusedCommandCatalog.Default);
        services.AddSingleton<PolicyEvaluator>();
        services.AddSingleton<IWorkspacePathResolver, CanonicalPathResolver>();
        services.AddSingleton<IWorkspaceFileAccess, BoundedFileAccess>();
        services.AddSingleton<IProcessRunner, HardenedProcessRunner>();
        services.AddSingleton<IGitWorktreeService, GitWorktreeService>();
        services.AddScoped<WorkspaceToolService>();
        services.AddScoped<ApprovalsService>();
        services.AddScoped<ChangeSetService>();

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
