using System.Collections.Concurrent;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Idempotency;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Todos;
using SharpAgent.Domain.Workspaces;

namespace SharpAgent.TestKit.Fakes;

/// <summary>
/// In-memory implementations of the application ports. They return the SAME aggregate
/// instance between calls (identity semantics), matching EF scoped-context behavior
/// closely enough for application-level tests; persistence-level guarantees are proven
/// separately against real SQLite in Infrastructure.Tests.
/// </summary>
public sealed class MemorySessionRepository : ISessionRepository
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public Func<Session, DateTimeOffset>? SortKeyOverride { get; set; }

    public IReadOnlyCollection<Session> Snapshot => [.. _sessions.Values];

    public Task AddAsync(Session session, CancellationToken cancellationToken)
    {
        if (!_sessions.TryAdd(session.Id, session))
        {
            throw new InvalidOperationException("Session already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<Session?> FindAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.TryGetValue(sessionId, out var session) ? session : null);

    public Task<IReadOnlyList<Session>> ListRecentAsync(int page, int pageSize, bool includeArchived, CancellationToken cancellationToken)
    {
        var query = _sessions.Values.AsEnumerable();
        if (!includeArchived)
        {
            query = query.Where(static session => session.ArchivedAtUtc is null);
        }

        var list = query
            .OrderByDescending(session => SortKeyOverride?.Invoke(session) ?? session.UpdatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult<IReadOnlyList<Session>>(list);
    }
}

public sealed class MemoryWorkspaceRepository : IWorkspaceRepository
{
    private readonly ConcurrentDictionary<string, Workspace> _workspaces = new();

    public IReadOnlyCollection<Workspace> Snapshot => [.. _workspaces.Values];

    public Task AddAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!_workspaces.TryAdd(workspace.Id, workspace))
        {
            throw new InvalidOperationException("Workspace already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<Workspace?> FindAsync(string workspaceId, CancellationToken cancellationToken) =>
        Task.FromResult(_workspaces.TryGetValue(workspaceId, out var workspace) ? workspace : null);

    public Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Workspace>>([.. _workspaces.Values]);
}

public sealed class MemoryModelProfileRepository : IModelProfileRepository
{
    private readonly ConcurrentDictionary<string, ModelProfile> _profiles = new();

    public IReadOnlyCollection<ModelProfile> Snapshot => [.. _profiles.Values];

    public void Seed(ModelProfile profile) => _profiles[profile.Id] = profile;

    public Task<ModelProfile?> FindAsync(string modelProfileId, CancellationToken cancellationToken) =>
        Task.FromResult(_profiles.TryGetValue(modelProfileId, out var profile) ? profile : null);

    public Task AddAsync(ModelProfile profile, CancellationToken cancellationToken)
    {
        _profiles[profile.Id] = profile;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ModelProfile>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ModelProfile>>([.. _profiles.Values]);
}

public sealed class MemoryPolicyProfileRepository : IPolicyProfileRepository
{
    private readonly ConcurrentDictionary<string, PolicyProfile> _policies = new();

    public void Seed(PolicyProfile policy) => _policies[policy.Id] = policy;

    public Task<PolicyProfile?> FindAsync(string policyProfileId, CancellationToken cancellationToken) =>
        Task.FromResult(_policies.TryGetValue(policyProfileId, out var policy) ? policy : null);

    public Task AddAsync(PolicyProfile profile, CancellationToken cancellationToken)
    {
        _policies[profile.Id] = profile;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PolicyProfile>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PolicyProfile>>([.. _policies.Values]);
}

public sealed class MemoryTodoRepository : ITodoRepository
{
    private readonly ConcurrentDictionary<string, TodoItem> _todos = new();

    public Task AddRangeAsync(IReadOnlyList<TodoItem> todos, CancellationToken cancellationToken)
    {
        foreach (var todo in todos)
        {
            _todos[todo.Id] = todo;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TodoItem>> ListBySessionAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TodoItem>>(
            [.. _todos.Values.Where(todo => todo.SessionId == sessionId).OrderBy(static todo => todo.Sequence)]);
}

public sealed class MemoryAuditEventRepository : IAuditEventRepository
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, AuditEvent>> _events = new();

    /// <summary>Directly inserts an event bypassing sequence allocation; used to prove uniqueness rules.</summary>
    public Func<AuditEvent, Task>? InsertHook { get; set; }

    public Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        var perSession = _events.GetOrAdd(auditEvent.SessionId, static _ => new ConcurrentDictionary<long, AuditEvent>());
        if (!perSession.TryAdd(auditEvent.Sequence, auditEvent))
        {
            throw new InvalidOperationException("Duplicate audit event sequence for session.");
        }

        return InsertHook is null ? Task.CompletedTask : InsertHook(auditEvent);
    }

    public Task<IReadOnlyList<AuditEvent>> ReplayAsync(string sessionId, CancellationToken cancellationToken)
    {
        var perSession = _events.TryGetValue(sessionId, out var events)
            ? events
            : new ConcurrentDictionary<long, AuditEvent>();

        return Task.FromResult<IReadOnlyList<AuditEvent>>(
            [.. perSession.Values.OrderBy(static auditEvent => auditEvent.Sequence)]);
    }

    public Task<long> GetMaxSequenceAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(
            _events.TryGetValue(sessionId, out var events) && !events.IsEmpty
                ? events.Keys.Max()
                : 0L);
}

public sealed class MemoryRunLeaseRepository : IRunLeaseRepository
{
    private readonly ConcurrentDictionary<string, RunLease> _leases = new();

    public Task AddAsync(RunLease lease, CancellationToken cancellationToken)
    {
        if (!_leases.TryAdd(lease.Id, lease))
        {
            throw new InvalidOperationException("Lease already exists.");
        }

        return Task.CompletedTask;
    }

    public Task<RunLease?> FindActiveBySessionAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult<RunLease?>(
            _leases.Values.FirstOrDefault(lease => lease.SessionId == sessionId && lease.ReleasedAtUtc is null));

    public Task ReleaseForRunAsync(string runId, DateTimeOffset releasedAtUtc, CancellationToken cancellationToken)
    {
        foreach (var lease in _leases.Values.Where(lease => lease.RunId == runId && lease.ReleasedAtUtc is null))
        {
            lease.Release(releasedAtUtc);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RunLease>> FindUnreleasedAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunLease>>(
            [.. _leases.Values.Where(lease => lease.ReleasedAtUtc is null)]);
}

public sealed class MemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _records = new();

    public int SaveCalls { get; private set; }

    public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_records.TryGetValue(key, out var record) ? record : null);

    public Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken)
    {
        SaveCalls++;
        if (!_records.TryAdd(record.Key, record))
        {
            throw new InvalidOperationException("Duplicate idempotency key.");
        }

        return Task.CompletedTask;
    }

    public Task<int> DeleteExpiredAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        var expired = _records.Values.Where(record => record.ExpiresAtUtc <= cutoffUtc).ToList();
        foreach (var record in expired)
        {
            _records.TryRemove(record.Key, out _);
        }

        return Task.FromResult(expired.Count);
    }
}


