using System.Text.Json;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Common;
using SharpAgent.Application.Security;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Common;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Workspaces;
using SharpAgent.Domain.Tools;

namespace SharpAgent.Application.Tools;

/// <summary>
/// The ONLY path from a runtime tool proposal to the filesystem or a process.
/// Order is fixed (FR-040..FR-047): re-canonicalize targets → evaluate policy →
/// allow / gate behind a single-use fingerprinted approval / deny. Executors are
/// unreachable until the policy decision — and where required the approval — has
/// been persisted. Reads run against the registered workspace root; every
/// side-effecting action runs inside a disposable per-run worktree, never the
/// registered base checkout.
/// </summary>
public sealed class WorkspaceToolService(
    ISessionRepository sessions,
    IWorkspaceRepository workspaces,
    IModelProfileRepository modelProfiles,
    IPolicyProfileRepository policies,
    IApprovalRequestRepository approvals,
    IChangeSetStore changeSets,
    IToolExecutionRepository toolExecutions,
    IAuditEventRepository events,
    IUnitOfWork unitOfWork,
    IClock clock,
    IWorkspacePathResolver pathResolver,
    IWorkspaceFileAccess fileAccess,
    IProcessRunner processRunner,
    IGitWorktreeService worktreeService,
    FocusedCommandCatalog commandCatalog,
    ISessionEventPublisher? eventPublisher = null)
{
    public const int MaxReadCharacters = 8_000;
    public const int MaxListEntries = 200;
    public const int MaxSearchResults = 50;
    public const int CommandTimeoutSeconds = 600;

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Proposes one tool action; nothing executes without a policy decision.</summary>
    public async Task<ToolProposalResult> ProposeAsync(
        ToolProposal proposal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        var session = await sessions.FindAsync(proposal.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("session", proposal.SessionId);
        var workspace = await workspaces.FindAsync(proposal.WorkspaceId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("workspace", proposal.WorkspaceId);
        var run = RequireActiveRun(session, proposal.RunId);

        var policyProfile = await policies.FindAsync(session.PolicyProfileId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("policy profile", session.PolicyProfileId);
        var modelProfile = await modelProfiles.FindAsync(session.ModelProfileId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("model profile", session.ModelProfileId);

        // 1) The POLICY DECISION comes first (FR-040): denied proposals never reach
        //    worktree creation, target resolution, files, or processes.
        var decision = PolicyEvaluator.Evaluate(session.Mode, proposal, policyProfile, modelProfile);

        await EmitEventAsync(session, run.Id, AuditEventTypes.PolicyDecision, new
        {
            action = proposal.Action.ToString(),
            outcome = decision.Outcome.ToString(),
            rule = decision.RuleMatched,
            reason = decision.SafeReason,
        }, cancellationToken).ConfigureAwait(false);

        switch (decision.Outcome)
        {
            case Domain.Tools.PolicyOutcome.Deny:
                return new ToolProposalResult.Denied(decision.SafeReason);
        }

        // Fail fast on unknown catalog commands BEFORE an approval is requested.
        if (proposal.Action == ToolAction.RunCommand
            && !commandCatalog.TryResolve(proposal.CommandName ?? string.Empty, out _))
        {
            throw ValidationException.ForField("commandName", "Command is not in the approved catalog.");
        }

        BoundaryRoots boundary;
        IReadOnlyList<ResolvedTarget> targets;
        string? patchContentHash;
        try
        {
            boundary = await ResolveBoundaryAsync(workspace, run, proposal.Action, cancellationToken).ConfigureAwait(false);

            // 2) Canonicalize every proposed target BEFORE any executor is reachable (FR-002).
            (targets, patchContentHash) = await ResolveTargetsAsync(boundary, proposal, cancellationToken).ConfigureAwait(false);
        }
        catch (WorkspaceEscapeException)
        {
            await EmitEventAsync(
                    session,
                    run.Id,
                    AuditEventTypes.WorkspaceDenied,
                    new { reason = "workspace_boundary" },
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        catch (ConflictException exception) when (exception.Code == "workspace_unavailable")
        {
            await EmitEventAsync(
                    session,
                    run.Id,
                    AuditEventTypes.WorkspaceDenied,
                    new { reason = "workspace_unavailable" },
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }

        if (decision.Outcome == Domain.Tools.PolicyOutcome.Allow)
        {
            return await ExecuteCoreAsync(
                session, run, boundary, proposal, targets, patchContentHash, approvalId: null, cancellationToken)
                .ConfigureAwait(false);
        }

        // 3) Single-use, expiring, fingerprinted approval (FR-043..FR-045).
        var fingerprint = ActionFingerprint.Compute(
            proposal, targets, workspace.CanonicalRootPath!, policyProfile.RulesJson, patchContentHash);
        var expiresAt = clock.UtcNow.AddMinutes(policyProfile.ApprovalExpiryMinutes);

        var payload = new ApprovalStoredPayload(proposal, targets, patchContentHash ?? string.Empty);
        var approval = ApprovalRequest.Create(
            proposal.RunId,
            proposal.SessionId,
            fingerprint,
            proposal.Action.ToString(),
            BuildSummary(proposal, targets),
            JsonSerializer.Serialize(targets.Select(static t => t.RelativePath).ToArray(), PayloadOptions),
            decision.SafeReason,
            clock.UtcNow,
            expiresAt,
            requestJson: JsonSerializer.Serialize(payload, PayloadOptions));

        await approvals.AddAsync(approval, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await EmitEventAsync(session, run.Id, AuditEventTypes.ApprovalRequested, new
        {
            approvalId = approval.Id,
            actionType = approval.ActionType,
            summary = SecretRedactor.Redact(approval.Summary),
            expiresAtUtc = expiresAt,
        }, cancellationToken).ConfigureAwait(false);

        return new ToolProposalResult.AwaitingApproval(approval.Id, fingerprint, expiresAt);
    }

    /// <summary>
    /// Executes the exact stored payload of one APPROVED approval. The fingerprint is
    /// recomputed from CURRENT state first; any drift refuses execution (FR-045).
    /// </summary>
    public async Task<ToolProposalResult> ExecuteApprovedAsync(
        string approvalId,
        CancellationToken cancellationToken = default)
    {
        var approval = await approvals.FindAsync(approvalId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("approval", approvalId);

        if (approval.Status != ApprovalStatus.Approved || approval.Decision != ApprovalDecision.ApproveOnce)
        {
            throw new ConflictException("approval_not_approved", "Only approved-once requests can execute.");
        }

        var payload = JsonSerializer.Deserialize<ApprovalStoredPayload>(approval.RequestJson, PayloadOptions);

        if (payload is null || payload.Proposal is null || payload.Targets is null)
        {
            throw new ConflictException(
                "approval_payload_invalid",
                "The stored approval payload could not be read; execution was refused.");
        }

        var session = await sessions.FindAsync(payload.Proposal.SessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("session", payload.Proposal.SessionId);
        var workspace = await workspaces.FindAsync(payload.Proposal.WorkspaceId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("workspace", payload.Proposal.WorkspaceId);
        var policyProfile = await policies.FindAsync(session.PolicyProfileId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("policy profile", session.PolicyProfileId);
        var run = RequireActiveRun(session, approval.RunId);

        var boundary = await ResolveBoundaryAsync(workspace, run, payload.Proposal.Action, cancellationToken).ConfigureAwait(false);

        // Re-canonicalize and recompute everything the fingerprint covers.
        var (targets, patchContentHash) = await ResolveTargetsAsync(boundary, payload.Proposal, cancellationToken).ConfigureAwait(false);
        var expectedFingerprint = ActionFingerprint.Compute(
            payload.Proposal, targets, workspace.CanonicalRootPath!, policyProfile.RulesJson, patchContentHash);

        if (!string.Equals(expectedFingerprint, approval.ActionFingerprint, StringComparison.Ordinal))
        {
            throw new ConflictException(
                "fingerprint_mismatch",
                "The action changed after approval; execution was refused.");
        }

        return await ExecuteCoreAsync(
            session, run, boundary, payload.Proposal, targets, patchContentHash, approval.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ execution core

    internal async Task<ToolProposalResult> ExecuteCoreAsync(
        Session session,
        AgentRun run,
        BoundaryRoots boundary,
        ToolProposal proposal,
        IReadOnlyList<ResolvedTarget> targets,
        string? patchContentHash,
        string? approvalId,
        CancellationToken cancellationToken)
    {
        var toolExecution = Domain.Tools.ToolExecution.Start(
            run.Id,
            proposal.Action.ToString(),
            Domain.Tools.PolicyOutcome.Allow,
            approvalId,
            clock.UtcNow,
            run.CorrelationId);

        try
        {
            await EmitEventAsync(session, run.Id, AuditEventTypes.ToolStarted, new
            {
                action = proposal.Action.ToString(),
                targetCount = targets.Count,
            }, cancellationToken).ConfigureAwait(false);

            var result = await DispatchAsync(session, boundary, proposal, targets, cancellationToken).ConfigureAwait(false);

            toolExecution.Complete(result.ExitCode, result.OutputPreview, result.Truncated, redactionApplied: true, clock.UtcNow);

            await EmitEventAsync(session, run.Id, AuditEventTypes.ToolCompleted, new
            {
                action = proposal.Action.ToString(),
                exitCode = result.ExitCode,
                truncated = result.Truncated,
            }, cancellationToken).ConfigureAwait(false);

            return new ToolProposalResult.Executed(result.OutputPreview, result.Truncated, RedactionApplied: true);
        }
        catch (WorkspaceEscapeException)
        {
            toolExecution.Fail("Target escaped the workspace boundary.", clock.UtcNow);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            toolExecution.Fail("The tool was cancelled.", clock.UtcNow);
            throw;
        }
        catch (Exception exception)
        {
            toolExecution.Fail("The tool failed inside the workspace boundary.", clock.UtcNow);

            await EmitEventAsync(session, run.Id, AuditEventTypes.ToolCompleted, new
            {
                action = proposal.Action.ToString(),
                failed = true,
                detail = exception.GetType().Name, // type name only; never raw messages
            }, CancellationToken.None).ConfigureAwait(false);

            throw;
        }
        finally
        {
            await toolExecutions.AddAsync(toolExecution, cancellationToken).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<(int? ExitCode, string OutputPreview, bool Truncated)> DispatchAsync(
        Session session,
        BoundaryRoots boundary,
        ToolProposal proposal,
        IReadOnlyList<ResolvedTarget> targets,
        CancellationToken cancellationToken)
    {
        switch (proposal.Action)
        {
            case ToolAction.ReadFile:
                {
                    var (content, truncated) = fileAccess.ReadTextBounded(targets[0], MaxReadCharacters);
                    return (0, SecretRedactor.Redact(content)!, truncated);
                }

            case ToolAction.ListDirectory:
                {
                    var entries = fileAccess.ListTopLevel(targets[0]);
                    var preview = string.Join('\n', entries
                        .Take(MaxListEntries)
                        .Select(static entry => entry.IsDirectory ? $"{entry.Name}/" : $"{entry.Name} ({entry.Length} bytes)"));
                    return (0, preview, entries.Count > MaxListEntries);
                }

            case ToolAction.SearchText:
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(proposal.SearchQuery);
                    var matches = fileAccess.SearchText(targets[0], proposal.SearchQuery, MaxSearchResults, out var searchTruncated);
                    return (0, SecretRedactor.Redact(string.Join('\n', matches))!, searchTruncated);
                }

            case ToolAction.RepositoryStatus:
                {
                    var status = processRunner.Run(GitStatusRequest(boundary.ExecutionRoot), cancellationToken);
                    var note = status.Succeeded ? string.Empty : " [git status failed]";
                    return (status.ExitCode, SecretRedactor.Redact(status.CombinedOutput + note)!, status.OutputTruncated);
                }

            case ToolAction.ApplyPatch:
                return await ApplyPatchAsync(session, boundary, proposal, cancellationToken).ConfigureAwait(false);

            case ToolAction.RunCommand:
                return RunCatalogCommand(proposal, boundary.ExecutionRoot, cancellationToken);

            default:
                throw new ArgumentOutOfRangeException(nameof(proposal), proposal.Action, null);
        }
    }

    private async Task<(int? ExitCode, string OutputPreview, bool Truncated)> ApplyPatchAsync(
        Session session,
        BoundaryRoots boundary,
        ToolProposal proposal,
        CancellationToken cancellationToken)
    {
        var changeSetId = proposal.ChangeSetId
                          ?? throw ValidationException.ForField("changeSetId", "Change set id is required.");
        var changeSet = await changeSets.FindAsync(changeSetId, cancellationToken).ConfigureAwait(false)
                        ?? throw new NotFoundException("change set", changeSetId);

        var applied = PatchApplicationService.Apply(changeSet, boundary.ExecutionRoot, pathResolver, fileAccess, clock);

        if (applied.AllApplied)
        {
            changeSet.MarkApplied(applied.SummaryText, clock.UtcNow);
        }
        else
        {
            changeSet.MarkFailed(applied.SummaryText, clock.UtcNow);
        }

        foreach (var relativePath in applied.AppliedFiles)
        {
            await EmitEventAsync(session, changeSet.RunId, AuditEventTypes.ChangeDetected, new
            {
                changeSetId = changeSet.Id,
                path = relativePath,
            }, cancellationToken).ConfigureAwait(false);
        }

        return (applied.AllApplied ? 0 : 1, applied.SummaryText, false);
    }

    private (int? ExitCode, string OutputPreview, bool Truncated) RunCatalogCommand(
        ToolProposal proposal,
        string workingRoot,
        CancellationToken cancellationToken)
    {
        if (!commandCatalog.TryResolve(proposal.CommandName ?? string.Empty, out var template))
        {
            throw ValidationException.ForField("commandName", "Command is not in the approved catalog.");
        }

        var arguments = template.BaseArguments.Concat(proposal.Arguments ?? []).ToArray();
        var result = processRunner.Run(new ProcessExecutionRequest(
            template.Executable,
            arguments,
            workingRoot,
            Timeout: TimeSpan.FromSeconds(CommandTimeoutSeconds),
            OutputLimitCharacters: MaxReadCharacters), cancellationToken);

        var exitNote = result.Succeeded
            ? string.Empty
            : $" [exit {(result.TimedOut ? "timeout" : result.Cancelled ? "cancelled" : result.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown")}]";

        return (result.ExitCode, SecretRedactor.Redact(result.CombinedOutput + exitNote)!, result.OutputTruncated);
    }

    private static ProcessExecutionRequest GitStatusRequest(string workingRoot) => new(
        Executable: "git",
        Arguments: ["status", "--porcelain=v1"],
        WorkingDirectory: workingRoot,
        Timeout: TimeSpan.FromSeconds(30),
        OutputLimitCharacters: MaxReadCharacters);

    // ------------------------------------------------------------------ resolution

    private async Task<(IReadOnlyList<ResolvedTarget> Targets, string? PatchContentHash)> ResolveTargetsAsync(
        BoundaryRoots boundary,
        ToolProposal proposal,
        CancellationToken cancellationToken)
    {
        switch (proposal.Action)
        {
            case ToolAction.ReadFile or ToolAction.ListDirectory or ToolAction.SearchText:
                ArgumentException.ThrowIfNullOrWhiteSpace(proposal.RelativePath);
                return ([pathResolver.Resolve(boundary.BoundaryForReads, proposal.RelativePath)], null);

            case ToolAction.RepositoryStatus:
            case ToolAction.RunCommand:
                return ([], null);

            case ToolAction.ApplyPatch:
                var changeSetId = proposal.ChangeSetId
                                  ?? throw ValidationException.ForField("changeSetId", "Change set id is required.");
                var changeSet = await changeSets.FindAsync(changeSetId, cancellationToken).ConfigureAwait(false)
                                ?? throw new NotFoundException("change set", changeSetId);

                var targets = changeSet.Files
                    .Select(static file => file.RelativePath)
                    .Select(path => pathResolver.Resolve(boundary.ExecutionRoot, path))
                    .ToList();

                var contentHash = ActionFingerprint.Sha256Hex(string.Join(
                    '\n',
                    changeSet.Files.Select(static f => f.AfterContentText ?? $"<binary:{f.RelativePath}>")));

                return (targets, contentHash);

            default:
                throw new ArgumentOutOfRangeException(nameof(proposal), proposal.Action, null);
        }
    }

    private async Task<BoundaryRoots> ResolveBoundaryAsync(
        Workspace workspace,
        AgentRun run,
        ToolAction action,
        CancellationToken cancellationToken)
    {
        var baseRoot = workspace.CanonicalRootPath
                       ?? throw new ConflictException("workspace_unavailable", "The workspace root has not been validated.");

        if (!RequiresWorktree(action))
        {
            return new BoundaryRoots(baseRoot, Worktree: null);
        }

        if (!string.IsNullOrEmpty(run.WorktreePath) && worktreeService.Exists(run.WorktreePath))
        {
            return new BoundaryRoots(baseRoot, run.WorktreePath);
        }

        var info = await worktreeService.CreateAsync(baseRoot, run.Id, cancellationToken).ConfigureAwait(false);
        run.AssignEnvironment(info.EnvironmentId, info.Path);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new BoundaryRoots(baseRoot, info.Path);
    }

    internal static bool RequiresWorktree(ToolAction action) =>
        action is ToolAction.ApplyPatch or ToolAction.RunCommand;

    private static AgentRun RequireActiveRun(Session session, string runId)
    {
        var run = session.Runs.FirstOrDefault(candidate => candidate.Id == runId)
                  ?? throw new ConflictException("no_active_run", "Proposals must target the session's active run.");

        if (!RunStateMachine.IsActive(run.Status))
        {
            throw new ConflictException("run_not_active", "The targeted run can no longer execute tools.");
        }

        return run;
    }

    // ------------------------------------------------------------------ summary + audit

    public static string BuildSummary(ToolProposal proposal, IReadOnlyList<ResolvedTarget> targets) => proposal.Action switch
    {
        ToolAction.ReadFile => $"Read {proposal.RelativePath}.",
        ToolAction.ListDirectory => $"List directory {proposal.RelativePath}.",
        ToolAction.SearchText => $"Search '{Truncate(proposal.SearchQuery, 40)}' in {proposal.RelativePath}.",
        ToolAction.RepositoryStatus => "Show repository working-tree status.",
        ToolAction.ApplyPatch when targets.Count > 0 =>
            $"Apply change set to {targets.Count} file(s): {Truncate(string.Join(", ", targets.Select(static t => t.RelativePath)), 160)}.",
        ToolAction.ApplyPatch => "Apply proposed change set.",
        ToolAction.RunCommand =>
            $"Run '{Truncate(proposal.CommandName, 24)} {Truncate(string.Join(' ', proposal.Arguments ?? []), 120)}' in the run worktree.",
        _ => Truncate(proposal.Action.ToString(), 60),
    };

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max] + '…';

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
}

/// <summary>Read boundary (registered root) plus optional worktree execution boundary.</summary>
public sealed record BoundaryRoots(string BoundaryForReads, string? Worktree)
{
    public string ExecutionRoot => Worktree ?? BoundaryForReads;
}







