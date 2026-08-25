using System.Diagnostics;
using SharpAgent.Application.Abstractions;

namespace SharpAgent.Api.Middleware;

/// <summary>
/// Establishes one bounded correlation id for each HTTP request, returns it to
/// the caller, and emits only safe request metadata into the structured log.
/// </summary>
public sealed class RequestObservabilityMiddleware(
    RequestDelegate next,
    ILogger<RequestObservabilityMiddleware> logger)
{
    private static readonly Action<ILogger, Exception?> LogRequestStarted =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(20, nameof(LogRequestStarted)),
            "request_started");

    private static readonly Action<ILogger, int, long, Exception?> LogRequestCompleted =
        LoggerMessage.Define<int, long>(
            LogLevel.Information,
            new EventId(21, nameof(LogRequestCompleted)),
            "request_completed statusCode={StatusCode} durationMs={DurationMs}");

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext)
    {
        var correlationId = CorrelationIds.Normalize(context.Request.Headers[CorrelationIds.HeaderName].FirstOrDefault());
        correlationContext.SetCurrent(correlationId);
        context.Items[CorrelationIds.HeaderName] = correlationId;
        context.Response.Headers[CorrelationIds.HeaderName] = correlationId;

        var stopwatch = Stopwatch.StartNew();
        using var scope = logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["correlationId"] = correlationId,
            ["requestMethod"] = context.Request.Method,
            ["requestPath"] = context.Request.Path.Value ?? "/",
        });

        if (logger.IsEnabled(LogLevel.Information))
        {
            LogRequestStarted(logger, null);
        }
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                LogRequestCompleted(logger, context.Response.StatusCode, stopwatch.ElapsedMilliseconds, null);
            }
        }
    }
}
