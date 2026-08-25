using SharpAgent.Domain.Sessions;

namespace SharpAgent.Application.Sessions;

/// <summary>Provider-neutral session projection. Never exposes EF entities or secrets.</summary>
public sealed record SessionDto(
    string Id,
    string WorkspaceId,
    string Task,
    SessionMode Mode,
    SessionStatus Status,
    string ModelProfileId,
    string PolicyProfileId,
    string? ActiveRunId,
    bool Archived,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<RunDto> Runs);

public sealed record RunDto(
    string Id,
    int Sequence,
    RunStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    string? StopReason,
    string? ResumeSourceRunId);

public sealed record SessionSummaryDto(
    string Id,
    string Task,
    SessionMode Mode,
    SessionStatus Status,
    string WorkspaceId,
    string ModelProfileId,
    string? ActiveRunId,
    bool Archived,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AuditEventDto(
    long Sequence,
    string Type,
    DateTimeOffset OccurredAtUtc,
    string PayloadJson,
    string? SessionId = null,
    string? RunId = null,
    string? EventId = null);

public sealed record SessionEventReplay(
    IReadOnlyList<AuditEventDto> Events,
    long LastSequence,
    bool HasGap);

public sealed record CreateSessionRequest(
    string WorkspaceId,
    string Task,
    SessionMode Mode,
    string ModelProfileId,
    string PolicyProfileId);

public sealed record StartRunRequest(string? Instruction, string? ResumeFromRunId);
