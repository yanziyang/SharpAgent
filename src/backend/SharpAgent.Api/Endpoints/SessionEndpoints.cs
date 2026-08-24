using Microsoft.AspNetCore.Mvc;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Sessions;

namespace SharpAgent.Api.Endpoints;

/// <summary>
/// Session command/query surface (functional spec section 10.1). All mutating routes
/// require an Idempotency-Key header.
/// </summary>
public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sessions = endpoints.MapGroup("/api/sessions");

        sessions.MapPost(
            string.Empty,
            async (
                [FromBody] CreateSessionRequest request,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                SessionService sessionService,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.Problem(
                        title: "Idempotency-Key header is required for this operation.",
                        statusCode: StatusCodes.Status400BadRequest,
                        extensions: new Dictionary<string, object?> { ["code"] = "idempotency_key_required" });
                }

                var dto = await sessionService
                    .CreateAsync(request, idempotencyKey, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Created($"/api/sessions/{dto.Id}", dto);
            });

        sessions.MapGet(
            "/{sessionId}",
            async (string sessionId, SessionService sessionService, CancellationToken cancellationToken) =>
                Results.Ok(await sessionService.GetAsync(sessionId, cancellationToken).ConfigureAwait(false)));

        sessions.MapGet(
            string.Empty,
            async (
                SessionService sessionService,
                CancellationToken cancellationToken,
                int page = 1,
                int pageSize = 20,
                bool includeArchived = false) =>
                Results.Ok(await sessionService
                    .ListAsync(page, pageSize, includeArchived, cancellationToken)
                    .ConfigureAwait(false)));

        sessions.MapPost(
            "/{sessionId}/runs",
            async (
                string sessionId,
                [FromBody] StartRunRequest? request,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                SessionService sessionService,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.Problem(
                        title: "Idempotency-Key header is required for this operation.",
                        statusCode: StatusCodes.Status400BadRequest,
                        extensions: new Dictionary<string, object?> { ["code"] = "idempotency_key_required" });
                }

                var result = await sessionService
                    .StartOrResumeAsync(sessionId, request ?? new StartRunRequest(null, null), idempotencyKey, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Accepted($"/api/sessions/{sessionId}", result);
            });

        sessions.MapPost(
            "/{sessionId}/cancel",
            async (
                string sessionId,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                SessionService sessionService,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.Problem(
                        title: "Idempotency-Key header is required for this operation.",
                        statusCode: StatusCodes.Status400BadRequest,
                        extensions: new Dictionary<string, object?> { ["code"] = "idempotency_key_required" });
                }

                var dto = await sessionService.CancelAsync(sessionId, idempotencyKey, cancellationToken).ConfigureAwait(false);
                return Results.Accepted($"/api/sessions/{sessionId}", dto);
            });

        sessions.MapPost(
            "/{sessionId}/archive",
            async (
                string sessionId,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                SessionService sessionService,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.Problem(
                        title: "Idempotency-Key header is required for this operation.",
                        statusCode: StatusCodes.Status400BadRequest,
                        extensions: new Dictionary<string, object?> { ["code"] = "idempotency_key_required" });
                }

                return Results.Ok(await sessionService.ArchiveAsync(sessionId, idempotencyKey, cancellationToken).ConfigureAwait(false));
            });

        sessions.MapPost(
            "/{sessionId}/restore",
            async (
                string sessionId,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                SessionService sessionService,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.Problem(
                        title: "Idempotency-Key header is required for this operation.",
                        statusCode: StatusCodes.Status400BadRequest,
                        extensions: new Dictionary<string, object?> { ["code"] = "idempotency_key_required" });
                }

                return Results.Ok(await sessionService.RestoreAsync(sessionId, idempotencyKey, cancellationToken).ConfigureAwait(false));
            });

        sessions.MapGet(
            "/{sessionId}/events",
            async (string sessionId, SessionService sessionService, CancellationToken cancellationToken) =>
                Results.Ok(await sessionService.ReplayEventsAsync(sessionId, cancellationToken).ConfigureAwait(false)));

        return endpoints;
    }
}
