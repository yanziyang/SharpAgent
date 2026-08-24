using SharpAgent.Domain.Changes;

namespace SharpAgent.Application.Abstractions;

/// <summary>
/// Persistence for proposed change sets. Snapshots give the patch applier the exact
/// approved content; aggregates keep review metadata (hashes, diffs, status).
/// </summary>
public interface IChangeSetStore
{
    Task AddAsync(ChangeSet changeSet, CancellationToken cancellationToken);

    Task<ChangeSet?> FindAsync(string changeSetId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChangeSet>> ListByRunAsync(string runId, CancellationToken cancellationToken);
}

/// <summary>Tool-execution evidence rows (bounded, redacted previews only).</summary>
public interface IToolExecutionRepository
{
    Task AddAsync(Domain.Tools.ToolExecution execution, CancellationToken cancellationToken);

    Task<IReadOnlyList<Domain.Tools.ToolExecution>> ListByRunAsync(string runId, CancellationToken cancellationToken);
}
