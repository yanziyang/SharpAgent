using SharpAgent.Api.Endpoints;

namespace SharpAgent.Api.Endpoints;

/// <summary>
/// Maps the public REST surface. Phase 1 adds sessions, workspaces, and catalogs;
/// SSE events, approvals, and provider validation arrive with later phases.
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

        endpoints
            .MapSessionEndpoints()
            .MapWorkspaceAndCatalogEndpoints();

        return endpoints;
    }
}
