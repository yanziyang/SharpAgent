using Microsoft.EntityFrameworkCore;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Tools;
using SharpAgent.Domain.Usage;
using SharpAgent.Infrastructure.Persistence;
using SharpAgent.Infrastructure.Tests.Support;
using Xunit;

namespace SharpAgent.Infrastructure.Tests.Persistence;

public sealed class ObservabilityRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    [Fact]
    public async Task Aggregates_run_approval_tool_provider_policy_and_workspace_facts()
    {
        await _database.InitializeAsync();

        await using (var writer = _database.OpenContext())
        {
            var session = Domain.Sessions.Session.CreateNew("ws", "observe", SessionMode.Plan, "model", "policy", Now);
            var completedRun = session.BeginRun(Now);
            session.CompleteActiveRun("done", Now.AddSeconds(2));
            var interruptedRun = session.BeginRun(Now.AddSeconds(3), resumeSourceRunId: completedRun.Id);
            session.InterruptActiveRun("paused", Now.AddSeconds(4));

            await writer.Sessions.AddAsync(session);

            var approved = ApprovalRequest.Create(
                completedRun.Id,
                session.Id,
                "fp-approved",
                "patch",
                "Apply patch",
                "[]",
                null,
                Now,
                Now.AddMinutes(5));
            approved.Resolve(ApprovalDecision.ApproveOnce, Now.AddSeconds(1));

            var expired = ApprovalRequest.Create(
                interruptedRun.Id,
                session.Id,
                "fp-expired",
                "command",
                "Run tests",
                "[]",
                null,
                Now,
                Now.AddMinutes(5));
            expired.Expire(Now.AddSeconds(1));
            await writer.ApprovalRequests.AddRangeAsync([approved, expired]);

            var tool = ToolExecution.Start(
                completedRun.Id,
                "run_command",
                PolicyOutcome.Allow,
                approved.Id,
                Now);
            tool.Fail("bounded failure", Now.AddSeconds(1));
            await writer.ToolExecutions.AddAsync(tool);

            var usage = UsageRecord.StartNew(
                completedRun.Id,
                session.Id,
                "DeepSeek",
                "model",
                Now);
            usage.Record(10, 20, 0.30m, 100, Now.AddSeconds(1));
            await writer.UsageRecords.AddAsync(usage);

            await writer.AuditEvents.AddRangeAsync(
            [
                Event(session, completedRun, AuditEventTypes.Status, "{}", Now.AddSeconds(1)),
                Event(session, completedRun, AuditEventTypes.RunFailed, "{}", Now.AddSeconds(1)),
                Event(session, completedRun, AuditEventTypes.ProviderFallback, "{}", Now.AddSeconds(1)),
                Event(session, completedRun, AuditEventTypes.ContextCompacted, "{}", Now.AddSeconds(1)),
                Event(session, completedRun, AuditEventTypes.PolicyDecision, "{\"outcome\":\"Deny\"}", Now.AddSeconds(1)),
                Event(session, completedRun, AuditEventTypes.WorkspaceDenied, "{}", Now.AddSeconds(1)),
                Event(session, interruptedRun, AuditEventTypes.Status, "{}", Now.AddSeconds(3).AddMilliseconds(500)),
            ]);
            await writer.SaveChangesAsync();
        }

        await using var context = _database.OpenContext();
        var result = await new EfObservabilityQueryRepository(context)
            .QueryAsync(Now.AddHours(-1), CancellationToken.None);

        Assert.Equal(1, result.SessionStateCounts[SessionStatus.Interrupted]);
        Assert.NotNull(result.AverageRunDurationSeconds);
        Assert.NotNull(result.AverageTimeToFirstStatusSeconds);
        Assert.Equal(1, result.ApprovalOutcomeCounts[ApprovalStatus.Approved]);
        Assert.Equal(1, result.ApprovalOutcomeCounts[ApprovalStatus.Expired]);
        Assert.Equal(1, result.ToolFailureCount);
        Assert.Equal(1, result.ProviderFailureCount);
        Assert.Equal(1, result.ProviderFallbackCount);
        Assert.Equal(1, result.InterruptedRunCount);
        Assert.Equal(1, result.ResumedRunCount);
        Assert.Equal(1, result.ContextCompactionCount);
        Assert.Equal(1, result.PolicyDenialCount);
        Assert.Equal(1, result.WorkspaceDenialCount);

        var providerUsage = Assert.Single(result.ProviderUsage);
        Assert.Equal("DeepSeek", providerUsage.Provider);
        Assert.Equal(10, providerUsage.InputTokens);
        Assert.Equal(20, providerUsage.OutputTokens);
        Assert.Equal(0.30m, providerUsage.EstimatedCostUsd);
    }

    private static AuditEvent Event(
        Domain.Sessions.Session session,
        AgentRun run,
        string type,
        string payload,
        DateTimeOffset occurredAtUtc)
    {
        var sequence = session.ReserveNextEventSequence();
        return AuditEvent.Create(session.Id, run.Id, sequence, type, payload, occurredAtUtc, run.CorrelationId);
    }

    public void Dispose() => _database.Dispose();
}
