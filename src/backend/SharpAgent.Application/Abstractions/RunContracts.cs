using Microsoft.Extensions.AI;
using SharpAgent.Domain.Sessions;

namespace SharpAgent.Application.Abstractions;

/// <summary>Explicit per-run limits enforced by the runtime (FR-035).</summary>
public sealed record RunLimits(
    int MaxToolCalls,
    TimeSpan MaxDuration,
    decimal? MaxEstimatedCostUsd,
    decimal? InputUsdPerMillionTokens,
    decimal? OutputUsdPerMillionTokens);

public enum RunStopReason
{
    Completed = 0,
    AwaitingApproval = 1,
    Cancelled = 2,
    LimitReached = 3,
    ProviderError = 4,
    PolicyDenied = 5,
}

/// <summary>Safe outcome of one runtime execution; never carries raw provider data.</summary>
public sealed record RunOutcome(
    RunStopReason StopReason,
    string? SafeMessage,
    int ToolCallCount);

/// <summary>Canonical run event kinds mapped onto the functional spec 9.3 vocabulary.</summary>
public enum RunEventKind
{
    AssistantSummary = 0,
    TodoCreated = 1,
    TodoUpdated = 2,
    ToolStarted = 3,
    ToolOutput = 4,
    ToolCompleted = 5,
    ContextCompacted = 6,
    Status = 7,
    UsageUpdated = 8,
    ProviderFallback = 9,
}

/// <summary>
/// One canonical, server-safe run event. All text is bounded and redacted before
/// persistence; provider or MAF types never escape the runtime adapter.
/// </summary>
public sealed record RunEvent(
    RunEventKind Kind,
    string? Text,
    string? TodoId,
    string? TodoText,
    string? ToolName,
    string? Detail,
    DateTimeOffset OccurredAtUtc);

/// <summary>Append-only sink for canonical run events (persisted, then published later).</summary>
public interface IRunEventSink
{
    Task EmitAsync(RunEvent runEvent, CancellationToken cancellationToken);
}

/// <summary>
/// Everything one runtime execution needs. The chat client is provider-neutral and
/// built by the orchestrator from the profile adapter; retained state arrives as a
/// safe compacted summary plus todos so resumes never replay raw history.
/// </summary>
public sealed record RunContext(
    string SessionId,
    string RunId,
    string WorkspaceId,
    string WorkspaceRootPath,
    string? WorktreePath,
    SessionMode Mode,
    string Task,
    string? Instruction,
    IChatClient ChatClient,
    IToolProposalBridge ToolBridge,
    RunLimits Limits,
    IReadOnlyList<string> RetainedTodos,
    string? CompactedHistorySummary,
    IReadOnlyList<string> DecisionsSummary,
    string CorrelationId = "",
    string Provider = "",
    string ModelProfileId = "");

/// <summary>
/// Deterministic runtime boundary (plan section 11). The MAF implementation can be
/// replaced by a fake without touching policy, persistence, API, or UI.
/// </summary>
public interface IAgentRuntime
{
    Task<RunOutcome> RunAsync(RunContext context, IRunEventSink sink, CancellationToken cancellationToken);
}
