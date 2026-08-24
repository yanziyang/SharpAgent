using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Todos;
using SharpAgent.Domain.Workspaces;

namespace SharpAgent.Infrastructure.Persistence;

/// <summary>EF Core implementations of the application persistence ports.</summary>
public sealed class EfSessionRepository(SharpAgentDbContext context) : ISessionRepository
{
    public async Task AddAsync(Session session, CancellationToken cancellationToken)
    {
        await context.Sessions.AddAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public Task<Session?> FindAsync(string sessionId, CancellationToken cancellationToken) =>
        context.Sessions
            .Include(static session => session.Runs)
            .FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    public async Task<IReadOnlyList<Session>> ListRecentAsync(
        int page,
        int pageSize,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var query = context.Sessions.AsNoTracking();

        if (!includeArchived)
        {
            query = query.Where(static session => session.ArchivedAtUtc == null);
        }

        var list = await query
            .OrderByDescending(static session => session.UpdatedAtUtc)
            .ThenBy(static session => session.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return list.AsReadOnly();
    }
}

public sealed class EfWorkspaceRepository(SharpAgentDbContext context) : IWorkspaceRepository
{
    public async Task AddAsync(Workspace workspace, CancellationToken cancellationToken) =>
        await context.Workspaces.AddAsync(workspace, cancellationToken).ConfigureAwait(false);

    public Task<Workspace?> FindAsync(string workspaceId, CancellationToken cancellationToken) =>
        context.Workspaces.FirstOrDefaultAsync(workspace => workspace.Id == workspaceId, cancellationToken);

    public async Task<IReadOnlyList<Workspace>> ListAsync(CancellationToken cancellationToken)
    {
        var list = await context.Workspaces
            .AsNoTracking()
            .OrderBy(static workspace => workspace.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return list.AsReadOnly();
    }
}

public sealed class EfAuditEventRepository(SharpAgentDbContext context) : IAuditEventRepository
{
    public async Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
        await context.AuditEvents.AddAsync(auditEvent, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<AuditEvent>> ReplayAsync(string sessionId, CancellationToken cancellationToken)
    {
        var list = await context.AuditEvents
            .AsNoTracking()
            .Where(auditEvent => auditEvent.SessionId == sessionId)
            .OrderBy(static auditEvent => auditEvent.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return list.AsReadOnly();
    }
}

public sealed class EfTodoRepository(SharpAgentDbContext context) : ITodoRepository
{
    public async Task AddRangeAsync(IReadOnlyList<TodoItem> todos, CancellationToken cancellationToken) =>
        await context.TodoItems.AddRangeAsync(todos, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<TodoItem>> ListBySessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var list = await context.TodoItems
            .AsNoTracking()
            .Where(todo => todo.SessionId == sessionId)
            .OrderBy(static todo => todo.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return list.AsReadOnly();
    }
}

