using SharpAgent.Application.Common;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Changes;
using SharpAgent.Domain.Sessions;

namespace SharpAgent.Application.Tools;

public sealed record ProposeFileChange(
    string RelativePath,

    /// <summary>New text content; null with Delete for deletions, null+not-Delete marks binary (unsupported in MVP apply).</summary>
    string? NewContentText,
    bool Delete);

/// <summary>
/// Creates named change-set proposals against the CURRENT state of the run's
/// execution boundary. Before-hashes are captured now so later application can
/// refuse stale patches (FR-031, AC-02).
/// </summary>
public sealed class ChangeSetService(
    IChangeSetStore changeSets,
    ISessionRepository sessions,
    IWorkspaceRepository workspaces,
    IWorkspacePathResolver pathResolver,
    IWorkspaceFileAccess fileAccess,
    IClock clock)
{
    public async Task<ChangeSet> ProposeAsync(
        string sessionId,
        IReadOnlyList<ProposeFileChange> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            throw ValidationException.ForField("files", "At least one file change is required.");
        }

        var session = await sessions.FindAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("session", sessionId);

        var run = session.Runs.OrderByDescending(static candidate => candidate.Sequence).FirstOrDefault()
                  ?? throw new ConflictException("no_active_run", "Change sets belong to an executed run.");

        var workspace = await workspaces.FindAsync(session.WorkspaceId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("workspace", session.WorkspaceId);
        var baseRoot = workspace.CanonicalRootPath
                       ?? throw new ConflictException("workspace_unavailable", "The workspace root has not been validated.");

        // Proposal-time evidence is captured from the execution boundary when one
        // exists (a worktree), otherwise from the registered root.
        var evidenceRoot = WorkspaceToolService.RequiresWorktree(ToolAction.ApplyPatch) && !string.IsNullOrEmpty(run.WorktreePath)
            ? run.WorktreePath
            : baseRoot;

        var changeSet = ChangeSet.CreateNew(run.Id, clock.UtcNow);

        foreach (var file in files)
        {
            var target = pathResolver.Resolve(evidenceRoot, file.RelativePath); // escape => throws
            var beforeHash = fileAccess.FileHash(target) ?? string.Empty;

            var changeType = file.Delete
                ? FileChangeType.Deleted
                : beforeHash == string.Empty ? FileChangeType.Added : FileChangeType.Modified;

            var entry = changeSet.AddFile(file.RelativePath, changeType, clock.UtcNow);
            var afterHash = file.NewContentText is null
                ? null
                : SharpAgent.Application.Tools.ActionFingerprint.Sha256Hex(file.NewContentText);

            entry.RecordProposalEvidence(
                beforeHash: beforeHash,
                afterHash: file.Delete ? null : afterHash,
                diffText: BuildDiffPreview(file, beforeHash),
                afterContentText: file.NewContentText,
                clock.UtcNow);
        }

        await changeSets.AddAsync(changeSet, cancellationToken).ConfigureAwait(false);
        return changeSet;
    }

    public async Task<IReadOnlyList<ChangeSet>> ListByRunAsync(string runId, CancellationToken cancellationToken = default) =>
        await changeSets.ListByRunAsync(runId, cancellationToken).ConfigureAwait(false);

    private static string BuildDiffPreview(ProposeFileChange file, string beforeHash)
    {
        if (file.Delete || file.NewContentText is null)
        {
            return string.Empty;
        }

        const int maxLines = 60;
        var newLines = file.NewContentText.Split('\n');
        var head = newLines.Take(maxLines).Select(static line => "+" + line.TrimEnd('\r'));
        var suffix = newLines.Length > maxLines ? new[] { $"… (+{newLines.Length - maxLines} more lines)" } : [];
        return $"--- a/{file.RelativePath} (before {beforeHash[..Math.Min(12, beforeHash.Length)]})" + '\n'
               + $"+++ b/{file.RelativePath}" + '\n'
               + string.Join('\n', head.Concat(suffix));
    }
}


