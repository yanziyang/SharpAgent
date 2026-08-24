using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpAgent.Domain.Sessions;

namespace SharpAgent.Infrastructure.Persistence.Configurations;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("Sessions");

        builder.HasKey(static session => session.Id);
        builder.Property(static session => session.Id).HasMaxLength(64);

        builder.Property(static session => session.WorkspaceId).HasMaxLength(64);
        builder.Property(static session => session.ModelProfileId).HasMaxLength(64);
        builder.Property(static session => session.PolicyProfileId).HasMaxLength(64);
        builder.Property(static session => session.ActiveRunId).HasMaxLength(64);
        builder.Property(static session => session.Task).HasMaxLength(SessionServiceLimits.MaxTaskLength);
        builder.Property(static session => session.LastInstruction).HasMaxLength(4_000);

        builder.Property(static session => session.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(static session => session.Mode).HasConversion<string>().HasMaxLength(16);

        builder.Property(static session => session.Version).IsConcurrencyToken();

        builder.HasIndex(static session => session.WorkspaceId);
        builder.HasIndex(static session => session.Status);
        builder.HasIndex(static session => session.UpdatedAtUtc);

        builder.HasMany(static session => session.Runs)
            .WithOne()
            .HasForeignKey(static run => run.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal static class SessionServiceLimits
{
    public const int MaxTaskLength = 8_000;
}

internal sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("AgentRuns");

        builder.HasKey(static run => run.Id);
        builder.Property(static run => run.Id).HasMaxLength(64);
        builder.Property(static run => run.SessionId).HasMaxLength(64);
        builder.Property(static run => run.ResumeSourceRunId).HasMaxLength(64);
        builder.Property(static run => run.CorrelationId).HasMaxLength(64);
        builder.Property(static run => run.ExecutionEnvironmentId).HasMaxLength(128);

        builder.Property(static run => run.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(static run => run.ExecutionEnvironmentId).HasMaxLength(128);
        builder.Property(static run => run.WorktreePath).HasMaxLength(1_024);

        // One active run per session, enforced by the database itself.
        builder.HasIndex(static run => new { run.SessionId })
            .IsUnique()
            .HasFilter("\"Status\" IN ('Planning', 'Executing', 'AwaitingApproval', 'Reviewing')");

        builder.HasIndex(static run => new { run.SessionId, run.Sequence }).IsUnique();

        builder.Property(static run => run.StopReason).HasMaxLength(500);
        builder.Property(static run => run.ContextSummary).HasMaxLength(16_000);
        builder.Property(static run => run.FinalSummary).HasMaxLength(16_000);
    }
}

internal sealed class RunLeaseConfiguration : IEntityTypeConfiguration<RunLease>
{
    public void Configure(EntityTypeBuilder<RunLease> builder)
    {
        builder.ToTable("RunLeases");

        builder.HasKey(static lease => lease.Id);
        builder.Property(static lease => lease.Id).HasMaxLength(64);
        builder.Property(static lease => lease.SessionId).HasMaxLength(64);
        builder.Property(static lease => lease.RunId).HasMaxLength(64);

        // At most one live lease per session; released leases keep history.
        builder.HasIndex(static lease => new { lease.SessionId })
            .IsUnique()
            .HasFilter("\"ReleasedAtUtc\" IS NULL");

        builder.HasIndex(static lease => lease.RunId);
    }
}
