using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Tools;

namespace SharpAgent.Application.Tools;

/// <summary>
/// Provider-neutral tool proposal produced by the runtime. Paths are
/// workspace-relative; commands reference the focused catalog by name.
/// </summary>
public sealed record ToolProposal(
    string SessionId,
    string RunId,
    string WorkspaceId,
    ToolAction Action,
    string? RelativePath = null,
    string? SearchQuery = null,
    string? CommandName = null,
    IReadOnlyList<string>? Arguments = null,
    string? ChangeSetId = null,
    string? Content = null,
    string? OldText = null,
    string? NewText = null,
    string? NamePattern = null,
    bool Recursive = false);

public enum ToolAction
{
    ReadFile = 0,
    ListDirectory = 1,
    SearchText = 2,
    RepositoryStatus = 3,

    /// <summary>Apply a previously proposed change set inside the run worktree.</summary>
    ApplyPatch = 4,

    /// <summary>Run a focused test/build command from the approved catalog.</summary>
    RunCommand = 5,

    /// <summary>Propose creating or replacing a text file in the run worktree.</summary>
    WriteFile = 6,

    /// <summary>Propose a unique old-text to new-text edit in the run worktree.</summary>
    EditFile = 7,

    /// <summary>Find workspace files by bounded name pattern.</summary>
    FindFiles = 8,
}

/// <summary>Pure policy verdict before any executor is reachable (FR-040/FR-041).</summary>
public sealed record PolicyDecision(PolicyOutcome Outcome, string RuleMatched, string SafeReason);

public sealed record ResolvedTarget(string AbsolutePath, string RelativePath);

/// <summary>Result of proposing one tool action.</summary>
public abstract record ToolProposalResult
{
    /// <summary>Auto-allowed read-only action that already executed.</summary>
    public sealed record Executed(
        string OutputPreview,
        bool OutputTruncated,
        bool RedactionApplied) : ToolProposalResult;

    /// <summary>Blocked before any filesystem/process access.</summary>
    public sealed record Denied(string Reason) : ToolProposalResult;

    /// <summary>Blocked because the current mode forbids this action entirely (Plan mode).</summary>
    public sealed record ModeForbidden(string Reason) : ToolProposalResult;

    /// <summary>Awaiting a single-use developer decision; nothing has executed.</summary>
    public sealed record AwaitingApproval(
        string ApprovalId,
        string ActionFingerprint,
        DateTimeOffset ExpiresAtUtc) : ToolProposalResult;
}


