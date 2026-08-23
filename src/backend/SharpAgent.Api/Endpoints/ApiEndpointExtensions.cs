using SharpAgent.Application.Health;

namespace SharpAgent.Api.Endpoints;

/// <summary>
/// Maps the public REST surface. Phase 0 exposes health only; later phases add
/// workspaces, profiles, sessions, SSE, and approvals per the functional spec section 10.1.
/// </summary>
public static class ApiEndpointExtensions
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapGet(
            "/health",
            async (HealthQueryService health, CancellationToken cancellationToken) =>
                Results.Ok(await health.ProbeAsync(cancellationToken).ConfigureAwait(false)));

        return endpoints;
    }
}
