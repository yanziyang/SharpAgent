using SharpAgent.Api.Endpoints;

namespace SharpAgent.Api.Endpoints;

/// <summary>
/// Maps the public REST surface: health, dashboard, sessions, catalogs, approvals,
/// and the replayable session event stream.
/// </summary>
public static class ApiEndpointExtensions
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapGet(
            "/health",
            async (SharpAgent.Application.Health.HealthQueryService health, CancellationToken cancellationToken) =>
                Results.Ok(await health.ProbeAsync(cancellationToken).ConfigureAwait(false)));

        api.MapGet(
            "/dashboard",
            async (
                int? periodDays,
                SharpAgent.Application.Dashboard.DashboardQueryService dashboard,
                CancellationToken cancellationToken) =>
                Results.Ok(await dashboard
                    .GetAsync(periodDays ?? SharpAgent.Application.Dashboard.DashboardQueryService.DefaultPeriodDays, cancellationToken)
                    .ConfigureAwait(false)));

        endpoints
            .MapSessionEndpoints()
            .MapWorkspaceAndCatalogEndpoints()
            .MapApprovalEndpoints();

        return endpoints;
    }
}


