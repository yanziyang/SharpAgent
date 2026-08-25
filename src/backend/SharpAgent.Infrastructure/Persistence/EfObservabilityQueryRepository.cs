using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Tools;

namespace SharpAgent.Infrastructure.Persistence;

/// <summary>
/// Reads bounded aggregate metrics from durable facts. Payloads are inspected
/// only for known, low-cardinality outcome markers and are never returned.
/// </summary>
public sealed class EfObservabilityQueryRepository(SharpAgentDbContext context) : IObservabilityQueryRepository
{
    private static readonly string[] MetricEventTypes =
    [
        AuditEventTypes.Status,
        AuditEventTypes.RunFailed,
        AuditEventTypes.ProviderFallback,
        AuditEventTypes.ContextCompacted,
        AuditEventTypes.PolicyDecision,
        AuditEventTypes.WorkspaceDenied,
    ];

    public async Task<ObservabilityQueryData> QueryAsync(
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken)
    {
        var sessionStates = await context.Sessions
            .AsNoTracking()
            .Where(session => session.UpdatedAtUtc >= sinceUtc)
            .Select(static session => session.Status)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var runs = await context.AgentRuns
            .AsNoTracking()
            .Where(run => run.StartedAtUtc >= sinceUtc)
            .Select(static run => new RunMetric(
                run.Id,
                run.Status,
                run.StartedAtUtc,
                run.EndedAtUtc,
                run.ResumeSourceRunId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var auditEvents = await context.AuditEvents
            .AsNoTracking()
            .Where(auditEvent => auditEvent.OccurredAtUtc >= sinceUtc
                && MetricEventTypes.Contains(auditEvent.Type))
            .Select(static auditEvent => new AuditMetric(
                auditEvent.RunId,
                auditEvent.Type,
                auditEvent.OccurredAtUtc,
                auditEvent.PayloadJson))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var approvalStatuses = await context.ApprovalRequests
            .AsNoTracking()
            .Where(approval => approval.CreatedAtUtc >= sinceUtc)
            .Select(static approval => approval.Status)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var toolStatuses = await context.ToolExecutions
            .AsNoTracking()
            .Where(tool => tool.StartedAtUtc >= sinceUtc)
            .Select(static tool => tool.Status)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var usage = await context.UsageRecords
            .AsNoTracking()
            .Where(record => record.RecordedAtUtc >= sinceUtc)
            .Select(static record => new UsageMetric(
                record.RunId,
                record.Provider,
                record.ModelProfileId,
                record.InputTokens,
                record.OutputTokens,
                record.EstimatedCostUsd))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var firstStatusByRun = auditEvents
            .Where(static auditEvent => auditEvent.Type == AuditEventTypes.Status && auditEvent.RunId is not null)
            .GroupBy(static auditEvent => auditEvent.RunId!, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Min(static auditEvent => auditEvent.OccurredAtUtc),
                StringComparer.Ordinal);

        var durations = runs
            .Where(static run => run.EndedAtUtc is not null)
            .Select(static run => (run.EndedAtUtc!.Value - run.StartedAtUtc).TotalSeconds)
            .Where(static seconds => seconds >= 0)
            .ToArray();

        var firstStatusDurations = runs
            .Where(run => firstStatusByRun.TryGetValue(run.Id, out var firstStatus)
                && firstStatus >= run.StartedAtUtc)
            .Select(run => (firstStatusByRun[run.Id] - run.StartedAtUtc).TotalSeconds)
            .Where(static seconds => seconds >= 0)
            .ToArray();

        var usageMetrics = usage
            .GroupBy(
                static record => (record.Provider, record.ModelProfileId),
                StringTupleComparer.Instance)
            .Select(static group => new ProviderUsageMetric(
                group.Key.Provider,
                group.Key.ModelProfileId,
                group.Sum(static record => record.InputTokens ?? 0),
                group.Sum(static record => record.OutputTokens ?? 0),
                group.Sum(static record => record.EstimatedCostUsd ?? 0m),
                group.Select(static record => record.RunId).Distinct(StringComparer.Ordinal).Count()))
            .ToArray();

        return new ObservabilityQueryData(
            sessionStates
                .GroupBy(static status => status)
                .ToDictionary(static group => group.Key, static group => group.Count()),
            durations.Length == 0 ? null : durations.Average(),
            firstStatusDurations.Length == 0 ? null : firstStatusDurations.Average(),
            approvalStatuses
                .GroupBy(static status => status)
                .ToDictionary(static group => group.Key, static group => group.Count()),
            toolStatuses.Count(static status => status == ToolExecutionStatus.Failed),
            auditEvents.Count(static auditEvent => auditEvent.Type == AuditEventTypes.RunFailed),
            auditEvents.Count(static auditEvent => auditEvent.Type == AuditEventTypes.ProviderFallback),
            runs.Count(static run => run.Status == RunStatus.Interrupted),
            runs.Count(static run => run.ResumeSourceRunId is not null),
            auditEvents.Count(static auditEvent => auditEvent.Type == AuditEventTypes.ContextCompacted),
            usageMetrics,
            auditEvents.Count(static auditEvent =>
                auditEvent.Type == AuditEventTypes.PolicyDecision && IsPolicyDenial(auditEvent.PayloadJson)),
            auditEvents.Count(static auditEvent => auditEvent.Type == AuditEventTypes.WorkspaceDenied));
    }

    private static bool IsPolicyDenial(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.TryGetProperty("outcome", out var outcome)
                && string.Equals(outcome.GetString(), "Deny", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record RunMetric(
        string Id,
        RunStatus Status,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        string? ResumeSourceRunId);

    private sealed record AuditMetric(
        string? RunId,
        string Type,
        DateTimeOffset OccurredAtUtc,
        string PayloadJson);

    private sealed record UsageMetric(
        string RunId,
        string Provider,
        string ModelProfileId,
        long? InputTokens,
        long? OutputTokens,
        decimal? EstimatedCostUsd);

    private sealed class StringTupleComparer : IEqualityComparer<(string Provider, string ModelProfileId)>
    {
        public static StringTupleComparer Instance { get; } = new();

        public bool Equals((string Provider, string ModelProfileId) x, (string Provider, string ModelProfileId) y) =>
            string.Equals(x.Provider, y.Provider, StringComparison.Ordinal)
            && string.Equals(x.ModelProfileId, y.ModelProfileId, StringComparison.Ordinal);

        public int GetHashCode((string Provider, string ModelProfileId) obj) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Provider),
                StringComparer.Ordinal.GetHashCode(obj.ModelProfileId));
    }
}
