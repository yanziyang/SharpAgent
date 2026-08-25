using System.Text.Json;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Common;
using SharpAgent.Application.Idempotency;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Common;
using SharpAgent.Domain.Tools;

namespace SharpAgent.Application.Tools;

public sealed record ResolveApprovalRequest(ApprovalDecision Decision, string? Comment);

public sealed record ApprovalDto(
    string Id,
    string RunId,
    string SessionId,
    string ActionType,
    string Summary,
    IReadOnlyList<string> AffectedPaths,
    string Status,
    DateTimeOffset ExpiresAtUtc);

public sealed record ApprovalResolutionOutcome(
    string ApprovalId,
    string ApprovalStatus,
    Domain.Sessions.SessionStatus SessionStatus,
    ToolProposalResult? ExecutionResult);

/// <summary>
/// Single-use approval decisions (FR-046/FR-047). Approve-once records the decision
/// then executes the exact fingerprinted action; deny returns a bounded result to
/// the runtime; cancel-run stops the owning run. Every decision is idempotent per
/// key and emits an audit event.
/// </summary>
public sealed class ApprovalsService(
    IApprovalRequestRepository approvals,
    ISessionRepository sessions,
    IAuditEventRepository events,
    IIdempotencyStore idempotencyStore,
    IUnitOfWork unitOfWork,
    IClock clock,
    WorkspaceToolService toolService,
    ISessionEventPublisher? eventPublisher = null)
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    private IdempotencyService Idempotency { get; } = new(idempotencyStore, clock);

    public async Task<ApprovalResolutionOutcome> ResolveAsync(
        string approvalId,
        ResolveApprovalRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalId);
        ArgumentNullException.ThrowIfNull(request);

        var requestHash = IdempotencyService.HashPayload(new { approvalId, request });

        var result = await Idempotency.ExecuteAsync(
            unitOfWork,
            idempotencyKey,
            OperationNames.ResolveApproval,
            requestHash,
            transactionCancellationToken => ResolveCoreAsync(approvalId, request, transactionCancellationToken),
            cancellationToken).ConfigureAwait(false);

        return result.Value;
    }

    public async Task<IReadOnlyList<ApprovalDto>> ListPendingAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var pending = await approvals.ListPendingBySessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return [.. pending.Select(ToDto)];
    }

    private async Task<ApprovalResolutionOutcome> ResolveCoreAsync(
        string approvalId,
        ResolveApprovalRequest request,
        CancellationToken cancellationToken)
    {
        var approval = await approvals.FindAsync(approvalId, cancellationToken).ConfigureAwait(false)
                       ?? throw new NotFoundException("approval", approvalId);

        if (approval.IsExpired(clock.UtcNow))
        {
            approval.Expire(clock.UtcNow);
            throw new ConflictException("approval_expired", "This approval expired before a decision was recorded.");
        }

        // Single-use guarantee (FR-045): any second resolution attempt is a conflict,
        // even with an identical decision.
        if (approval.Status != ApprovalStatus.Pending)
        {
            throw new ConflictException(
                "approval_already_resolved",
                "This approval was already resolved; approvals are single-use.");
        }

        ToolProposalResult? executionResult = null;

        switch (request.Decision)
        {
            case ApprovalDecision.ApproveOnce:
                approval.Resolve(ApprovalDecision.ApproveOnce, clock.UtcNow);
                break;

            case ApprovalDecision.Deny:
                approval.Resolve(ApprovalDecision.Deny, clock.UtcNow);
                break;

            case ApprovalDecision.CancelRun:
                approval.Resolve(ApprovalDecision.CancelRun, clock.UtcNow);
                await CancelOwningRunAsync(approval, cancellationToken).ConfigureAwait(false);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Decision, null);
        }

        var session = (await sessions.FindAsync(approval.SessionId, cancellationToken).ConfigureAwait(false))
                      ?? throw new NotFoundException("session", approval.SessionId);

        await EmitEventAsync(session, approval.RunId, AuditEventTypes.ApprovalResolved, new
        {
            approvalId = approval.Id,
            decision = request.Decision.ToString(),
            comment = string.IsNullOrWhiteSpace(request.Comment) ? null : Truncate(request.Comment, 300),
        }, cancellationToken).ConfigureAwait(false);

        if (request.Decision == ApprovalDecision.ApproveOnce)
        {
            // Execute immediately; ExecuteApprovedAsync re-validates the fingerprint.
            executionResult = await toolService.ExecuteApprovedAsync(approval.Id, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new ApprovalResolutionOutcome(approval.Id, approval.Status.ToString(), session.Status, executionResult);
    }

    private async Task CancelOwningRunAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        var session = (await sessions.FindAsync(approval.SessionId, cancellationToken).ConfigureAwait(false))
                      ?? throw new NotFoundException("session", approval.SessionId);

        if (session.ActiveRunId == approval.RunId)
        {
            session.CancelActiveRun("Cancelled from approval prompt.", clock.UtcNow);

            await EmitEventAsync(session, approval.RunId, AuditEventTypes.RunCancelled, new
            {
                reason = "cancelled_from_approval",
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static ApprovalDto ToDto(ApprovalRequest approval) => new(
        approval.Id,
        approval.RunId,
        approval.SessionId,
        approval.ActionType,
        approval.Summary,
        ParsePaths(approval.AffectedPathsJson),
        approval.Status.ToString(),
        approval.ExpiresAtUtc);

    private async Task EmitEventAsync(Session session, string? runId, string type, object payload, CancellationToken ct)
    {
        var sequence = session.ReserveNextEventSequence();
        var auditEvent = AuditEvent.Create(
            session.Id,
            runId,
            sequence,
            type,
            JsonSerializer.Serialize(payload, PayloadOptions),
            clock.UtcNow,
            session.Runs.FirstOrDefault(run => string.Equals(run.Id, runId, StringComparison.Ordinal))?.CorrelationId
                ?? DomainId.NewCorrelationId());

        await events.AddAsync(auditEvent, ct).ConfigureAwait(false);
        unitOfWork.RegisterAfterCommit(() => eventPublisher?.Publish(auditEvent));
    }

    private static List<string> ParsePaths(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, PayloadOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + '…';
}


