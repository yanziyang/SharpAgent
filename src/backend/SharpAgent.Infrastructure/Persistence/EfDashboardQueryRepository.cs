using Microsoft.EntityFrameworkCore;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Tools;

namespace SharpAgent.Infrastructure.Persistence;

public sealed class EfDashboardQueryRepository(SharpAgentDbContext context) : IDashboardQueryRepository
{
    public async Task<DashboardQueryData> QueryAsync(
        int recentSessionLimit,
        bool includeArchived,
        DateTimeOffset? sinceUtc,
        CancellationToken cancellationToken)
    {
        var sessions = context.Sessions.AsNoTracking();
        if (!includeArchived)
        {
            sessions = sessions.Where(static session => session.ArchivedAtUtc == null);
        }

        if (sinceUtc is not null)
        {
            sessions = sessions.Where(session => session.UpdatedAtUtc >= sinceUtc.Value);
        }

        var sessionIds = sessions.Select(static session => session.Id);
        var sessionStates = await sessions
            .GroupBy(static session => session.Status)
            .Select(static group => new { State = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var recentSessions = await sessions
            .OrderByDescending(static session => session.UpdatedAtUtc)
            .ThenBy(static session => session.Id)
            .Take(Math.Clamp(recentSessionLimit, 1, 50))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var runs = await context.AgentRuns
            .AsNoTracking()
            .Where(run => sessionIds.Contains(run.SessionId)
                && (sinceUtc == null || run.StartedAtUtc >= sinceUtc.Value))
            .Select(static run => new RunMetric(run.Id, run.Status, run.StartedAtUtc, run.EndedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var runIds = runs.Select(static run => run.Id).ToArray();
        var approvalCount = await context.ApprovalRequests
            .AsNoTracking()
            .Where(approval => sessionIds.Contains(approval.SessionId)
                && (sinceUtc == null || approval.CreatedAtUtc >= sinceUtc.Value))
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var toolFailureCount = runIds.Length == 0
            ? 0
            : await context.ToolExecutions
                .AsNoTracking()
                .Where(tool => runIds.Contains(tool.RunId)
                    && tool.Status == ToolExecutionStatus.Failed
                    && (sinceUtc == null || tool.StartedAtUtc >= sinceUtc.Value))
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);

        var providerFailureCount = await context.AuditEvents
            .AsNoTracking()
            .Where(auditEvent => sessionIds.Contains(auditEvent.SessionId)
                && auditEvent.Type == AuditEventTypes.RunFailed
                && (sinceUtc == null || auditEvent.OccurredAtUtc >= sinceUtc.Value))
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var contextCompactionCount = await context.AuditEvents
            .AsNoTracking()
            .Where(auditEvent => sessionIds.Contains(auditEvent.SessionId)
                && auditEvent.Type == AuditEventTypes.ContextCompacted
                && (sinceUtc == null || auditEvent.OccurredAtUtc >= sinceUtc.Value))
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var costs = runIds.Length == 0
            ? []
            : await context.UsageRecords
                .AsNoTracking()
                .Where(usage => runIds.Contains(usage.RunId) && usage.EstimatedCostUsd != null)
                .Select(static usage => usage.EstimatedCostUsd!.Value)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var durations = runs
            .Where(static run => run.EndedAtUtc is not null)
            .Select(static run => (run.EndedAtUtc!.Value - run.StartedAtUtc).TotalSeconds)
            .Where(static seconds => seconds >= 0)
            .ToArray();

        return new DashboardQueryData(
            sessionStates.ToDictionary(static row => row.State, static row => row.Count),
            runs.Count(static run => run.Status == RunStatus.Completed),
            durations.Length == 0 ? null : durations.Average(),
            approvalCount,
            toolFailureCount,
            providerFailureCount,
            contextCompactionCount,
            costs.Count == 0 ? null : costs.Sum(),
            recentSessions);
    }

    private sealed record RunMetric(
        string Id,
        RunStatus Status,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc);
}
