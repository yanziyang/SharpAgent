using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Common;
using SharpAgent.Application.Tools;
using SharpAgent.Domain.Common;

namespace SharpAgent.Api.ErrorHandling;

/// <summary>
/// Maps application/domain failures to stable problem codes the UI can translate
/// into actionable copy (Implementation Plan section 12.2).
/// </summary>
public sealed partial class SharpAgentProblemHandler(ILogger<SharpAgentProblemHandler> logger)
    : IExceptionHandler
{
    private static readonly Action<ILogger, string, int, string, string, Exception?> LogMapped =
        LoggerMessage.Define<string, int, string, string>(
            LogLevel.Information,
            new EventId(1, nameof(SharpAgentProblemHandler)),
            "Mapped {ExceptionType} to HTTP {Status} with code {ProblemCode} correlationId={CorrelationId}");

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, code, errors) = Map(exception);

        if (status is null)
        {
            return false; // Unhandled: let the default handler produce a safe 500.
        }

        var safeCorrelationId = httpContext.Items.TryGetValue(CorrelationIds.HeaderName, out var correlationId)
            && correlationId is string requestCorrelationId
            ? requestCorrelationId
            : CorrelationIds.Normalize(null);

        LogMapped(logger, exception.GetType().Name, status.Value, code, safeCorrelationId, null);
        httpContext.Response.Headers[CorrelationIds.HeaderName] = safeCorrelationId;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://sharpagent.local/problems/{code}",
        };
        problem.Extensions["code"] = code;
        problem.Extensions["correlationId"] = safeCorrelationId;

        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors;
        }

        httpContext.Response.StatusCode = status.Value;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static (int? Status, string Title, string Code, IReadOnlyDictionary<string, string[]>? Errors) Map(
        Exception exception) => exception switch
        {
            NotFoundException notFound => (
                StatusCodes.Status404NotFound,
                notFound.Message,
                "not_found",
                null),

            ConflictException conflict => (
                StatusCodes.Status409Conflict,
                conflict.Message,
                conflict.Code,
                null),

            ValidationException validation => (
                StatusCodes.Status400BadRequest,
                validation.Message,
                ValidationException.Code,
                validation.Errors),

            InvalidStateTransitionException invalidTransition => (
                StatusCodes.Status409Conflict,
                invalidTransition.Message,
                "invalid_transition",
                null),

            WorkspaceEscapeException workspaceEscape => (
                StatusCodes.Status409Conflict,
                workspaceEscape.Message,
                WorkspaceEscapeException.Code,
                null),

            DbUpdateConcurrencyException concurrency => (
                StatusCodes.Status409Conflict,
                "The session was modified by another request. Reload and retry.",
                "concurrency_conflict",
                null),

            _ => (null, string.Empty, string.Empty, null),
        };
}
