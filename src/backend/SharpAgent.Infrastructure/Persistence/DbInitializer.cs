using Microsoft.EntityFrameworkCore;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Common;
using SharpAgent.Domain.Sessions;
using SharpAgent.Infrastructure.Retention;

namespace SharpAgent.Infrastructure.Persistence;

/// <summary>
/// Startup persistence tasks: apply migrations, sweep abandoned runs to interrupted
/// (design section 5.2), and prune expired idempotency keys.
/// </summary>
public sealed class DbInitializer(
    IDbContextFactory<SharpAgentDbContext> contextFactory,
    IGitWorktreeService worktrees,
    RetentionCleanupService? retentionCleanup = null)
{
    public const string RestartedReason = "Service restarted while the run was active.";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        await SweepAbandonedRunsAsync(context, cancellationToken).ConfigureAwait(false);

        // Idempotency retention pruning happens opportunistically at startup:
        // every record whose expiry has passed is deleted.
        var nowUtc = DateTimeOffset.UtcNow;
        _ = await context.IdempotencyRecords
            .Where(record => record.ExpiresAtUtc <= nowUtc)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        if (retentionCleanup is not null)
        {
            await retentionCleanup.CleanupAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Any run left active by a previous process becomes interrupted and resumable.</summary>
    private async Task SweepAbandonedRunsAsync(SharpAgentDbContext context, CancellationToken cancellationToken)
    {
        var activeSessionIds = await context.Sessions
            .Where(session => session.ActiveRunId != null)
            .Select(static session => session.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var sessionId in activeSessionIds)
        {
            var session = await context.Sessions
                .Include(static candidate => candidate.Runs)
                .FirstOrDefaultAsync(candidate => candidate.Id == sessionId, cancellationToken)
                .ConfigureAwait(false);

            if (session?.ActiveRunId is null)
            {
                continue;
            }

            var abandonedRunId = session.ActiveRunId;
            var now = DateTimeOffset.UtcNow;

            try
            {
                session.InterruptActiveRun(RestartedReason, now);
            }
            catch (InvalidStateTransitionException)
            {
                // The run already reached a terminal state; nothing to sweep.
                continue;
            }

            var leases = await context.RunLeases
                .Where(lease => lease.RunId == abandonedRunId && lease.ReleasedAtUtc == null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var lease in leases)
            {
                lease.Release(now);
            }

            var abandonedRun = session.Runs.FirstOrDefault(candidate => candidate.Id == abandonedRunId);
            if (abandonedRun is not null && !string.IsNullOrEmpty(abandonedRun.WorktreePath))
            {
                try
                {
                    await worktrees.RemoveAsync(
                            new WorktreeInfo(abandonedRun.ExecutionEnvironmentId ?? "wt", abandonedRun.WorktreePath!),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Best-effort cleanup; retention sweeps handle leftovers later.
                }
            }

            var sequence = session.ReserveNextEventSequence();
            var auditEvent = AuditEvent.Create(
                session.Id,
                abandonedRunId,
                sequence,
                AuditEventTypes.Status,
                System.Text.Json.JsonSerializer.Serialize(
                    new { runId = abandonedRunId, status = "interrupted", reason = RestartedReason }),
                now,
                abandonedRun?.CorrelationId);
            await context.AuditEvents.AddAsync(auditEvent, cancellationToken).ConfigureAwait(false);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}


