using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Idempotency;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Todos;
using SharpAgent.Domain.Workspaces;

namespace SharpAgent.Application.Abstractions;

/// <summary>Session aggregate persistence. Implementations must load runs with the session.</summary>
public interface ISessionRepository
{
    Task AddAsync(Session session, CancellationToken cancellationToken);

    /// <summary>Returns the tracked aggregate including its runs, or null.</summary>
    Task<Session?> FindAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Newest first; archived sessions are excluded unless requested.</summary>
    Task<IReadOnlyList<Session>> ListRecentAsync(int page, int pageSize, bool includeArchived, CancellationToken cancellationToken);
}

public interface IWorkspaceRepository
{
    Task AddAsync(Workspace workspace, CancellationToken cancellationToken);

    Task<Workspace?> FindAsync(string workspaceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken);
}

public interface IModelProfileRepository
{
    Task<ModelProfile?> FindAsync(string modelProfileId, CancellationToken cancellationToken);

    Task AddAsync(ModelProfile profile, CancellationToken cancellationToken);

    Task<IReadOnlyList<ModelProfile>> ListAsync(CancellationToken cancellationToken);
}

public interface IPolicyProfileRepository
{
    Task<PolicyProfile?> FindAsync(string policyProfileId, CancellationToken cancellationToken);

    Task AddAsync(PolicyProfile profile, CancellationToken cancellationToken);

    Task<IReadOnlyList<PolicyProfile>> ListAsync(CancellationToken cancellationToken);
}

public interface ITodoRepository
{
    Task AddRangeAsync(IReadOnlyList<TodoItem> todos, CancellationToken cancellationToken);

    Task<IReadOnlyList<TodoItem>> ListBySessionAsync(string sessionId, CancellationToken cancellationToken);
}

/// <summary>Approval aggregate persistence (single-use decisions are immutable once recorded).</summary>
public interface IApprovalRequestRepository
{
    Task AddAsync(ApprovalRequest approval, CancellationToken cancellationToken);

    Task<ApprovalRequest?> FindAsync(string approvalId, CancellationToken cancellationToken);

    /// <summary>Pending approvals for a session ordered by creation time.</summary>
    Task<IReadOnlyList<ApprovalRequest>> ListPendingBySessionAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>The live pending approval for one run, if any.</summary>
    Task<ApprovalRequest?> FindPendingByRunAsync(string runId, CancellationToken cancellationToken);
}

/// <summary>Append-only audit event storage (no update or delete operations exist).</summary>
public interface IAuditEventRepository
{
    Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken);

    /// <summary>Events ordered by sequence ascending.</summary>
    Task<IReadOnlyList<AuditEvent>> ReplayAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Events strictly after a durable session sequence.</summary>
    Task<IReadOnlyList<AuditEvent>> ReplayAfterAsync(
        string sessionId,
        long afterSequence,
        CancellationToken cancellationToken);
}

public interface IRunLeaseRepository
{
    Task AddAsync(RunLease lease, CancellationToken cancellationToken);

    /// <summary>The live lease for a session, if any.</summary>
    Task<RunLease?> FindActiveBySessionAsync(string sessionId, CancellationToken cancellationToken);

    Task ReleaseForRunAsync(string runId, DateTimeOffset releasedAtUtc, CancellationToken cancellationToken);

    /// <summary>All unreleased leases; used by the startup recovery sweep.</summary>
    Task<IReadOnlyList<RunLease>> FindUnreleasedAsync(CancellationToken cancellationToken);
}

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken);

    Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken);

    /// <summary>Removes records created before the cutoff; returns the number deleted.</summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken);
}

/// <summary>Bounded transaction abstraction so services stay persistence-agnostic.</summary>
public interface IUnitOfWork
{
    /// <summary>Runs a synchronous publication only after the enclosing save commits.</summary>
    void RegisterAfterCommit(Action callback);

    /// <summary>Persists pending changes of all repositories in one atomic unit.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Runs the action inside one transaction, saving once at the end.</summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken);

    /// <summary>Runs the action inside one transaction and returns its result.</summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> action, CancellationToken cancellationToken);
}

