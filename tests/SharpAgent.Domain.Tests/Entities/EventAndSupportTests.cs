using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Common;
using SharpAgent.Domain.Idempotency;
using SharpAgent.Domain.Sessions;
using Xunit;

namespace SharpAgent.Domain.Tests.Entities;

public sealed class AuditEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_validates_type_and_sequence()
    {
        Assert.Throws<ArgumentException>(
            () => AuditEvent.Create("ses", "run", 1, string.Empty, "{}", Now));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AuditEvent.Create("ses", "run", 0, AuditEventTypes.SessionCreated, "{}", Now));
    }

    [Fact]
    public void Blank_payloads_default_to_an_empty_object()
    {
        var auditEvent = AuditEvent.Create("ses", null, 1, AuditEventTypes.SessionCreated, "  ", Now);

        Assert.Equal("{}", auditEvent.PayloadJson);
        Assert.StartsWith("evt_0000000001_", auditEvent.Id, StringComparison.Ordinal);
        Assert.Null(auditEvent.RunId);
    }

    [Fact]
    public void Every_canonical_event_type_is_recognized_and_unknowns_are_not()
    {
        // The full UI vocabulary (functional spec section 9.3), not a spot check.
        var canonicalTypes = new[]
        {
            AuditEventTypes.SessionCreated, AuditEventTypes.RunStarted, AuditEventTypes.Status,
            AuditEventTypes.AssistantSummary, AuditEventTypes.TodoCreated, AuditEventTypes.TodoUpdated,
            AuditEventTypes.ContextCompacted, AuditEventTypes.ToolProposed, AuditEventTypes.PolicyDecision,
            AuditEventTypes.ApprovalRequested, AuditEventTypes.ApprovalResolved, AuditEventTypes.ToolStarted,
            AuditEventTypes.ToolOutput, AuditEventTypes.ToolCompleted, AuditEventTypes.ChangeDetected,
            AuditEventTypes.ProviderFallback, AuditEventTypes.UsageUpdated, AuditEventTypes.RunCompleted,
            AuditEventTypes.RunFailed, AuditEventTypes.RunCancelled,
        };

        Assert.All(canonicalTypes, static type => Assert.True(AuditEventTypes.IsKnown(type)));

        Assert.False(AuditEventTypes.IsKnown("something_new"));
        Assert.False(AuditEventTypes.IsKnown(string.Empty));
    }
}

public sealed class IdempotencyRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_requires_key_hash_and_positive_retention()
    {
        Assert.Throws<ArgumentException>(
            () => IdempotencyRecord.Create(string.Empty, "op", "h", "{}", 200, Now, TimeSpan.FromHours(24)));
        Assert.Throws<ArgumentException>(
            () => IdempotencyRecord.Create("k", "op", string.Empty, "{}", 200, Now, TimeSpan.FromHours(24)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => IdempotencyRecord.Create("k", "op", "h", "{}", 200, Now, TimeSpan.Zero));
    }

    [Fact]
    public void Expiry_uses_the_documented_retention_window()
    {
        var record = IdempotencyRecord.Create("k", "create_session", "hash", "{}", 201, Now, TimeSpan.FromHours(24));

        Assert.False(record.IsExpired(Now.AddHours(23)));
        Assert.True(record.IsExpired(Now.AddHours(24)));
    }
}

public sealed class RunLeaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Acquire_requires_references_and_release_is_idempotent()
    {
        Assert.Throws<ArgumentException>(() => RunLease.Acquire(string.Empty, "run", Now));

        var lease = RunLease.Acquire("ses", "run", Now);
        Assert.Null(lease.ReleasedAtUtc);

        lease.Release(Now.AddMinutes(1));
        var firstRelease = lease.ReleasedAtUtc;
        lease.Release(Now.AddMinutes(2));

        Assert.Equal(firstRelease, lease.ReleasedAtUtc);
    }
}
