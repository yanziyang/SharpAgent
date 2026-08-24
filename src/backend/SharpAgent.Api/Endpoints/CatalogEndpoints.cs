using Microsoft.AspNetCore.Mvc;
using SharpAgent.Application.Profiles;
using SharpAgent.Application.Workspaces;

namespace SharpAgent.Api.Endpoints;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceAndCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var workspaces = endpoints.MapGroup("/api/workspaces");

        workspaces.MapGet(
            string.Empty,
            async (WorkspaceService workspaceService, CancellationToken cancellationToken) =>
                Results.Ok(await workspaceService.ListAsync(cancellationToken).ConfigureAwait(false)));

        workspaces.MapPost(
            string.Empty,
            async (
                [FromBody] RegisterWorkspaceRequest request,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                WorkspaceService workspaceService,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.Problem(
                        title: "Idempotency-Key header is required for this operation.",
                        statusCode: StatusCodes.Status400BadRequest,
                        extensions: new Dictionary<string, object?> { ["code"] = "idempotency_key_required" });
                }

                var dto = await workspaceService
                    .RegisterAsync(request, idempotencyKey, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created($"/api/workspaces/{dto.Id}", dto);
            });

        endpoints.MapGet(
            "/api/model-profiles",
            async (CatalogService catalog, CancellationToken cancellationToken) =>
                Results.Ok(await catalog.ListModelProfilesAsync(cancellationToken).ConfigureAwait(false)));

        endpoints.MapGet(
            "/api/policy-profiles",
            async (CatalogService catalog, CancellationToken cancellationToken) =>
                Results.Ok(await catalog.ListPolicyProfilesAsync(cancellationToken).ConfigureAwait(false)));

        return endpoints;
    }
}
