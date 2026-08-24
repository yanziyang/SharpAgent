using Microsoft.EntityFrameworkCore;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Changes;
using SharpAgent.Domain.Idempotency;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Todos;
using SharpAgent.Domain.Tools;
using SharpAgent.Domain.Usage;
using SharpAgent.Domain.Workspaces;
using SharpAgent.Infrastructure.Persistence.Configurations;

namespace SharpAgent.Infrastructure.Persistence;

/// <summary>EF Core context for SQLite. Mappings live in the Configurations namespace.</summary>
public sealed class SharpAgentDbContext(DbContextOptions<SharpAgentDbContext> options) : DbContext(options)
{
    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();

    public DbSet<RunLease> RunLeases => Set<RunLease>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<ModelProfile> ModelProfiles => Set<ModelProfile>();

    public DbSet<PolicyProfile> PolicyProfiles => Set<PolicyProfile>();

    public DbSet<TodoItem> TodoItems => Set<TodoItem>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    public DbSet<ToolExecution> ToolExecutions => Set<ToolExecution>();

    public DbSet<ChangeSet> ChangeSets => Set<ChangeSet>();

    public DbSet<FileChange> FileChanges => Set<FileChange>();

    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SharpAgentDbContext).Assembly);

        // SQLite stores timestamps as ISO-8601 text; keep UTC round-trip semantics.
        // Applied after configuration so every mapped date property is covered.
        var utc = new UtcTimestampConverter();
        var utcNullable = new NullableUtcTimestampConverter();

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(utc);
                    property.SetMaxLength(40);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(utcNullable);
                    property.SetMaxLength(40);
                }
            }
        }
    }

    /// <summary>Bumps the optimistic Version on every modified session (design section 4.2).</summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Session>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(Session.Version)).CurrentValue =
                    entry.OriginalValues.GetValue<int>(nameof(Session.Version)) + 1;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
