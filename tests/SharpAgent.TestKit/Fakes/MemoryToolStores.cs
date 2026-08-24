using System.Collections.Concurrent;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Changes;
using SharpAgent.Domain.Tools;

namespace SharpAgent.TestKit.Fakes;

public sealed class MemoryApprovalRepository : IApprovalRequestRepository
{
    private readonly ConcurrentDictionary<string, ApprovalRequest> _approvals = new();

    public IReadOnlyCollection<ApprovalRequest> Snapshot => [.. _approvals.Values];

    public Task AddAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (!_approvals.TryAdd(approval.Id, approval))
        {
            throw new InvalidOperationException("Approval already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<ApprovalRequest?> FindAsync(string approvalId, CancellationToken cancellationToken) =>
        Task.FromResult(_approvals.TryGetValue(approvalId, out var approval) ? approval : null);

    public Task<IReadOnlyList<ApprovalRequest>> ListPendingBySessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var list = _approvals.Values
            .Where(approval => approval.SessionId == sessionId && approval.Status == ApprovalStatus.Pending)
            .OrderBy(static approval => approval.CreatedAtUtc)
            .ToList();

        return Task.FromResult<IReadOnlyList<ApprovalRequest>>(list);
    }

    public Task<ApprovalRequest?> FindPendingByRunAsync(string runId, CancellationToken cancellationToken) =>
        Task.FromResult<ApprovalRequest?>(
            _approvals.Values.FirstOrDefault(approval => approval.RunId == runId && approval.Status == ApprovalStatus.Pending));
}

public sealed class MemoryChangeSetStore : IChangeSetStore
{
    private readonly ConcurrentDictionary<string, ChangeSet> _changeSets = new();

    public IReadOnlyCollection<ChangeSet> Snapshot => [.. _changeSets.Values];

    public Task AddAsync(ChangeSet changeSet, CancellationToken cancellationToken)
    {
        if (!_changeSets.TryAdd(changeSet.Id, changeSet))
        {
            throw new InvalidOperationException("Change set already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<ChangeSet?> FindAsync(string changeSetId, CancellationToken cancellationToken) =>
        Task.FromResult(_changeSets.TryGetValue(changeSetId, out var changeSet) ? changeSet : null);

    public Task<IReadOnlyList<ChangeSet>> ListByRunAsync(string runId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ChangeSet>>(
            [.. _changeSets.Values.Where(changeSet => changeSet.RunId == runId)]);
}

public sealed class MemoryToolExecutionRepository : IToolExecutionRepository
{
    private readonly ConcurrentDictionary<string, ToolExecution> _executions = new();

    public IReadOnlyCollection<ToolExecution> Snapshot => [.. _executions.Values];

    public Task AddAsync(ToolExecution execution, CancellationToken cancellationToken)
    {
        _executions[execution.Id] = execution;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ToolExecution>> ListByRunAsync(string runId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ToolExecution>>(
            [.. _executions.Values.Where(execution => execution.RunId == runId)]);
}
