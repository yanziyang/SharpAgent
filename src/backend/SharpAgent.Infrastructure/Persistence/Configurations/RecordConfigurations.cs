using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Changes;
using SharpAgent.Domain.Idempotency;
using SharpAgent.Domain.Todos;
using SharpAgent.Domain.Tools;
using SharpAgent.Domain.Usage;

namespace SharpAgent.Infrastructure.Persistence.Configurations;

internal sealed class TodoItemConfiguration : IEntityTypeConfiguration<TodoItem>
{
    public void Configure(EntityTypeBuilder<TodoItem> builder)
    {
        builder.ToTable("TodoItems");

        builder.HasKey(static todo => todo.Id);
        builder.Property(static todo => todo.Id).HasMaxLength(64);
        builder.Property(static todo => todo.SessionId).HasMaxLength(64);
        builder.Property(static todo => todo.RunId).HasMaxLength(64);
        builder.Property(static todo => todo.Text).HasMaxLength(1_000);

        builder.Property(static todo => todo.Status).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(static todo => new { todo.RunId, todo.Sequence }).IsUnique();
        builder.HasIndex(static todo => todo.SessionId);
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");

        builder.HasKey(static auditEvent => auditEvent.Id);
        builder.Property(static auditEvent => auditEvent.Id).HasMaxLength(80);
        builder.Property(static auditEvent => auditEvent.SessionId).HasMaxLength(64);
        builder.Property(static auditEvent => auditEvent.RunId).HasMaxLength(64);
        builder.Property(static auditEvent => auditEvent.Type).HasMaxLength(48);
        builder.Property(static auditEvent => auditEvent.PayloadJson).HasMaxLength(32_000);

        // Append-only invariant: unique monotonic sequence per session.
        builder.HasIndex(static auditEvent => new { auditEvent.SessionId, auditEvent.Sequence }).IsUnique();
    }
}

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("ApprovalRequests");

        builder.HasKey(static approval => approval.Id);
        builder.Property(static approval => approval.Id).HasMaxLength(64);
        builder.Property(static approval => approval.SessionId).HasMaxLength(64);
        builder.Property(static approval => approval.RunId).HasMaxLength(64);
        builder.Property(static approval => approval.ActionFingerprint).HasMaxLength(128);
        builder.Property(static approval => approval.ActionType).HasMaxLength(64);
        builder.Property(static approval => approval.Summary).HasMaxLength(2_000);
        builder.Property(static approval => approval.AffectedPathsJson).HasMaxLength(8_000);
        builder.Property(static approval => approval.RequestJson).HasMaxLength(32_000);
        builder.Property(static approval => approval.Reason).HasMaxLength(1_000);

        builder.Property(static approval => approval.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(static approval => approval.Decision).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(static approval => new { approval.RunId, approval.Status });
        builder.HasIndex(static approval => new { approval.SessionId, approval.Status });
        builder.HasIndex(static approval => approval.ExpiresAtUtc);
    }
}

internal sealed class ToolExecutionConfiguration : IEntityTypeConfiguration<ToolExecution>
{
    public void Configure(EntityTypeBuilder<ToolExecution> builder)
    {
        builder.ToTable("ToolExecutions");

        builder.HasKey(static execution => execution.Id);
        builder.Property(static execution => execution.Id).HasMaxLength(64);
        builder.Property(static execution => execution.RunId).HasMaxLength(64);
        builder.Property(static execution => execution.ToolName).HasMaxLength(64);
        builder.Property(static execution => execution.RequestSummary).HasMaxLength(4_000);
        builder.Property(static execution => execution.ApprovalId).HasMaxLength(64);
        builder.Property(static execution => execution.OutputPreview).HasMaxLength(8_000);
        builder.Property(static execution => execution.ErrorSummary).HasMaxLength(2_000);

        builder.Property(static execution => execution.PolicyOutcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(static execution => execution.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(static execution => execution.RunId);
    }
}

internal sealed class ChangeSetConfiguration : IEntityTypeConfiguration<ChangeSet>
{
    public void Configure(EntityTypeBuilder<ChangeSet> builder)
    {
        builder.ToTable("ChangeSets");

        builder.HasKey(static changeSet => changeSet.Id);
        builder.Property(static changeSet => changeSet.Id).HasMaxLength(64);
        builder.Property(static changeSet => changeSet.RunId).HasMaxLength(64);
        builder.Property(static changeSet => changeSet.Summary).HasMaxLength(2_000);

        builder.Property(static changeSet => changeSet.Status).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(static changeSet => changeSet.RunId);
    }
}

internal sealed class FileChangeConfiguration : IEntityTypeConfiguration<FileChange>
{
    public void Configure(EntityTypeBuilder<FileChange> builder)
    {
        builder.ToTable("FileChanges");

        builder.HasKey(static fileChange => fileChange.Id);
        builder.Property(static fileChange => fileChange.Id).HasMaxLength(64);
        builder.Property(static fileChange => fileChange.ChangeSetId).HasMaxLength(64);
        builder.Property(static fileChange => fileChange.RelativePath).HasMaxLength(512);
        builder.Property(static fileChange => fileChange.BeforeHash).HasMaxLength(128);
        builder.Property(static fileChange => fileChange.AfterHash).HasMaxLength(128);
        builder.Property(static fileChange => fileChange.DiffText).HasMaxLength(32_000);

        builder.Property(static fileChange => fileChange.ChangeType).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(static fileChange => fileChange.ChangeSetId);
    }
}

internal sealed class UsageRecordConfiguration : IEntityTypeConfiguration<UsageRecord>
{
    public void Configure(EntityTypeBuilder<UsageRecord> builder)
    {
        builder.ToTable("UsageRecords");

        builder.HasKey(static usage => usage.Id);
        builder.Property(static usage => usage.Id).HasMaxLength(64);
        builder.Property(static usage => usage.RunId).HasMaxLength(64);
        builder.Property(static usage => usage.SessionId).HasMaxLength(64);
        builder.Property(static usage => usage.Provider).HasMaxLength(32);
        builder.Property(static usage => usage.ModelProfileId).HasMaxLength(64);

        builder.HasIndex(static usage => usage.RunId);
        builder.HasIndex(static usage => usage.SessionId);
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        builder.HasKey(static record => record.Key);
        builder.Property(static record => record.Key).HasMaxLength(200);
        builder.Property(static record => record.Operation).HasMaxLength(64);
        builder.Property(static record => record.RequestHash).HasMaxLength(64);
        builder.Property(static record => record.ResponseJson).HasMaxLength(32_000);

        builder.HasIndex(static record => record.ExpiresAtUtc);
    }
}

