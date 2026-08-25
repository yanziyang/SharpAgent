using System.Globalization;
using System.Text.Json;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Sessions;
using SharpAgent.Domain.Auditing;

namespace SharpAgent.Api.Endpoints;

/// <summary>Negotiated audit replay/live stream endpoint.</summary>
public static class SessionEventEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Default heartbeat required by the functional SSE contract.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    public static async Task<IResult> HandleAsync(
        HttpContext context,
        string sessionId,
        SessionService sessionService,
        ISessionEventPublisher eventPublisher,
        CancellationToken cancellationToken)
    {
        if (!WantsEventStream(context))
        {
            return Results.Ok(await sessionService.ReplayEventsAsync(sessionId, cancellationToken).ConfigureAwait(false));
        }

        if (!TryReadLastEventId(context, out var afterSequence, out var error))
        {
            return Results.Problem(
                title: error,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "invalid_last_event_id" });
        }

        // Register before replaying so an event committed during the database
        // read is either present in replay or waiting in this channel.
        await using var subscription = eventPublisher.Subscribe(sessionId);
        var replay = await sessionService
            .ReplayEventsWithMetadataAsync(sessionId, afterSequence, cancellationToken)
            .ConfigureAwait(false);

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Append("X-Accel-Buffering", "no");
        await context.Response.StartAsync(cancellationToken).ConfigureAwait(false);

        var lastSequence = afterSequence;
        if (replay.HasGap)
        {
            await WriteReplayGapAsync(context.Response, cancellationToken).ConfigureAwait(false);
        }

        foreach (var auditEvent in replay.Events)
        {
            if (auditEvent.Sequence <= lastSequence)
            {
                continue;
            }

            await WriteReplayEventAsync(context.Response, sessionId, auditEvent, cancellationToken)
                .ConfigureAwait(false);
            lastSequence = auditEvent.Sequence;
        }

        await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        var pendingRead = subscription.ReadAsync(cancellationToken).AsTask();
        while (!cancellationToken.IsCancellationRequested)
        {
            var heartbeat = Task.Delay(HeartbeatInterval, cancellationToken);
            var completed = await Task.WhenAny(pendingRead, heartbeat).ConfigureAwait(false);

            if (completed == heartbeat)
            {
                await context.Response.WriteAsync(": heartbeat\n\n", cancellationToken).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            var auditEvent = await pendingRead.ConfigureAwait(false);
            if (auditEvent is null)
            {
                break;
            }

            pendingRead = subscription.ReadAsync(cancellationToken).AsTask();
            if (auditEvent.Sequence <= lastSequence)
            {
                continue;
            }

            if (auditEvent.Sequence > lastSequence + 1)
            {
                await WriteReplayGapAsync(context.Response, cancellationToken).ConfigureAwait(false);
            }

            await WriteLiveEventAsync(context.Response, auditEvent, cancellationToken).ConfigureAwait(false);
            lastSequence = auditEvent.Sequence;
        }

        return Results.StatusCode(StatusCodes.Status200OK);
    }

    private static bool WantsEventStream(HttpContext context) =>
        context.Request.Headers.Accept.ToString()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(static value => value.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase));

    private static bool TryReadLastEventId(HttpContext context, out long sequence, out string error)
    {
        var value = context.Request.Headers["Last-Event-ID"].ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            sequence = 0;
            error = string.Empty;
            return true;
        }

        if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out sequence)
            && sequence >= 0)
        {
            error = string.Empty;
            return true;
        }

        error = "Last-Event-ID must be a non-negative session event sequence.";
        sequence = 0;
        return false;
    }

    private static async Task WriteReplayEventAsync(
        HttpResponse response,
        string sessionId,
        AuditEventDto auditEvent,
        CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(
            new
            {
                sequence = auditEvent.Sequence,
                sessionId,
                runId = auditEvent.RunId,
                correlationId = auditEvent.CorrelationId,
                eventId = auditEvent.EventId,
                type = auditEvent.Type,
                occurredAtUtc = auditEvent.OccurredAtUtc,
                payload = ParsePayload(auditEvent.PayloadJson),
            },
            JsonOptions);

        await response.WriteAsync(
                $"id: {auditEvent.Sequence}\nevent: {SafeEventType(auditEvent.Type)}\ndata: {data}\n\n",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteLiveEventAsync(
        HttpResponse response,
        AuditEvent auditEvent,
        CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(
            new
            {
                eventId = auditEvent.Id,
                sequence = auditEvent.Sequence,
                sessionId = auditEvent.SessionId,
                runId = auditEvent.RunId,
                correlationId = auditEvent.CorrelationId,
                occurredAtUtc = auditEvent.OccurredAtUtc,
                type = auditEvent.Type,
                payload = ParsePayload(auditEvent.PayloadJson),
            },
            JsonOptions);

        await response.WriteAsync(
                $"id: {auditEvent.Sequence}\nevent: {SafeEventType(auditEvent.Type)}\ndata: {data}\n\n",
                cancellationToken)
            .ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Task WriteReplayGapAsync(HttpResponse response, CancellationToken cancellationToken)
    {
        var data = JsonSerializer.Serialize(
            new
            {
                code = "replay_gap",
                message = "Event history is not contiguous; refetch the session projection.",
            },
            JsonOptions);

        return response.WriteAsync($"event: status\ndata: {data}\n\n", cancellationToken);
    }

    private static JsonElement ParsePayload(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var document = JsonDocument.Parse("{}");
            return document.RootElement.Clone();
        }
    }

    private static string SafeEventType(string type) =>
        string.IsNullOrWhiteSpace(type) || !AuditEventTypes.IsKnown(type)
            ? "status"
            : type.Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
}
