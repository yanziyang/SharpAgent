using Microsoft.EntityFrameworkCore;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Changes;
using SharpAgent.Domain.Tools;

namespace SharpAgent.Infrastructure.Persistence;

public sealed class EfApprovalRequestRepository(SharpAgentDbContext context) : IApprovalRequestRepository
{
    public async Task AddAsync(ApprovalRequest approval, CancellationToken cancellationToken) =>
        await context.ApprovalRequests.AddAsync(approval, cancellationToken).ConfigureAwait(false);

    public Task<ApprovalRequest?> FindAsync(string approvalId, CancellationToken cancellationToken) =>
        context.ApprovalRequests.FirstOrDefaultAsync(approval => approval.Id == approvalId, cancellationToken);

    public async Task<IReadOnlyList<ApprovalRequest>> ListPendingBySessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var list = await context.ApprovalRequests
            .AsNoTracking()
            .Where(approval => approval.SessionId == sessionId && approval.Status == ApprovalStatus.Pending)
            .OrderBy(static approval => approval.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return list.AsReadOnly();
    }

    public Task<ApprovalRequest?> FindPendingByRunAsync(string runId, CancellationToken cancellationToken) =>
        context.ApprovalRequests.FirstOrDefaultAsync(
            approval => approval.RunId == runId && approval.Status == ApprovalStatus.Pending,
            cancellationToken);
}

public sealed class EfChangeSetStore(SharpAgentDbContext context) : IChangeSetStore
{
    public async Task AddAsync(ChangeSet changeSet, CancellationToken cancellationToken) =>
        await context.ChangeSets.AddAsync(changeSet, cancellationToken).ConfigureAwait(false);

    public Task<ChangeSet?> FindAsync(string changeSetId, CancellationToken cancellationToken) =>
        context.ChangeSets
            .Include(static changeSet => changeSet.Files)
            .FirstOrDefaultAsync(changeSet => changeSet.Id == changeSetId, cancellationToken);

    public async Task<IReadOnlyList<ChangeSet>> ListByRunAsync(string runId, CancellationToken cancellationToken)
    {
        var list = await context.ChangeSets
            .AsNoTracking()
            .Where(changeSet => changeSet.RunId == runId)
            .OrderBy(static changeSet => changeSet.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return list.AsReadOnly();
    }
}

public sealed class EfToolExecutionRepository(SharpAgentDbContext context) : IToolExecutionRepository
{
    public async Task AddAsync(ToolExecution execution, CancellationToken cancellationToken) =>
        await context.ToolExecutions.AddAsync(execution, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ToolExecution>> ListByRunAsync(string runId, CancellationToken cancellationToken)
    {
        var list = await context.ToolExecutions
            .AsNoTracking()
            .Where(execution => execution.RunId == runId)
            .OrderBy(static execution => execution.StartedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return list.AsReadOnly();
    }
}

