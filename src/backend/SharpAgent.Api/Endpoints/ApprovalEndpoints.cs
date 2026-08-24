using Microsoft.AspNetCore.Mvc;
using SharpAgent.Application.Tools;

namespace SharpAgent.Api.Endpoints;

/// <summary>Approval decision surface (functional spec section 10.1/10.4).</summary>
public static class ApprovalEndpoints
{
    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var approvals = endpoints.MapGroup("/api/approvals");

        approvals.MapPost(
            "/{approvalId}/resolve",
            async (
                string approvalId,
                [FromBody] ResolveApprovalRequest request,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                ApprovalsService approvalsService,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return Results.Problem(
                        title: "Idempotency-Key header is required for this operation.",
                        statusCode: StatusCodes.Status400BadRequest,
                        extensions: new Dictionary<string, object?> { ["code"] = "idempotency_key_required" });
                }

                var outcome = await approvalsService
                    .ResolveAsync(approvalId, request, idempotencyKey, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Accepted($"/api/sessions/{approvalId}", outcome);
            });

        return endpoints;
    }
}
