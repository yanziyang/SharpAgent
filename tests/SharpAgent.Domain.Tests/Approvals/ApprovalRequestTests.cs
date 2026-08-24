using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Common;
using Xunit;

namespace SharpAgent.Domain.Tests.Approvals;

public sealed class ApprovalRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    private static ApprovalRequest NewPending(
        DateTimeOffset? expiresAt = null,
        string fingerprint = "fp_abc123") =>
        ApprovalRequest.Create(
            "run_1",
            fingerprint,
            "apply_patch",
            "Update src/Pricing.tsx and its focused test.",
            """["src/Pricing.tsx","src/Pricing.test.tsx"]""",
            "The task requires a file modification.",
            Now,
            expiresAt ?? Now.AddMinutes(10));

    [Fact]
    public void Create_requires_fingerprint_and_future_expiry()
    {
        Assert.Throws<ArgumentException>(
            () => NewPending(fingerprint: string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewPending(expiresAt: Now));
    }

    [Theory]
    [InlineData(ApprovalDecision.ApproveOnce, ApprovalStatus.Approved)]
    [InlineData(ApprovalDecision.Deny, ApprovalStatus.Denied)]
    [InlineData(ApprovalDecision.CancelRun, ApprovalStatus.Cancelled)]
    public void Resolve_records_the_exactly_once_decision(ApprovalDecision decision, ApprovalStatus expected)
    {
        var approval = NewPending();

        var status = approval.Resolve(decision, Now.AddMinutes(1));

        Assert.Equal(expected, status);
        Assert.Equal(decision, approval.Decision);
        Assert.NotNull(approval.ResolvedAtUtc);
    }

    [Fact]
    public void Second_resolution_is_rejected()
    {
        var approval = NewPending();
        approval.Resolve(ApprovalDecision.ApproveOnce, Now.AddMinutes(1));

        Assert.Throws<InvalidStateTransitionException>(
            () => approval.Resolve(ApprovalDecision.Deny, Now.AddMinutes(2)));
        // Re-approving the same decision is also a rejection: single-use.
        Assert.Throws<InvalidStateTransitionException>(
            () => approval.Resolve(ApprovalDecision.ApproveOnce, Now.AddMinutes(2)));
    }

    [Fact]
    public void Expiry_applies_only_to_pending_approvals()
    {
        var pending = NewPending(expiresAt: Now.AddSeconds(30));
        Assert.True(pending.IsExpired(Now.AddSeconds(31)));
        Assert.False(pending.IsExpired(Now.AddSeconds(29)));

        pending.Expire(Now.AddSeconds(31));
        Assert.Equal(ApprovalStatus.Expired, pending.Status);

        var decided = NewPending();
        decided.Resolve(ApprovalDecision.Deny, Now.AddMinutes(1));

        decided.Expire(Now.AddMinutes(2)); // no-op
        Assert.Equal(ApprovalStatus.Denied, decided.Status);
    }
}
