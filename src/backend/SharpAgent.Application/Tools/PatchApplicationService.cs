using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Changes;

namespace SharpAgent.Application.Tools;

public sealed record PatchApplicationResult(bool AllApplied, string SummaryText, IReadOnlyList<string> AppliedFiles);

/// <summary>
/// Bounded patch applier. Files are applied ONLY inside the resolved execution
/// boundary (the run worktree). Every target is re-canonicalized immediately before
/// its write; before-hashes must still match or the change set fails atomically.
/// </summary>
public static class PatchApplicationService
{
    public static PatchApplicationResult Apply(
        ChangeSet changeSet,
        string executionRoot,
        IWorkspacePathResolver pathResolver,
        IWorkspaceFileAccess fileAccess,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        ArgumentNullException.ThrowIfNull(clock);

        var applied = new List<string>();

        foreach (var file in changeSet.Files)
        {
            // Re-canonicalize IMMEDIATELY before touching the file (FR-002).
            ResolvedTarget target;
            try
            {
                target = pathResolver.Resolve(executionRoot, file.RelativePath);
            }
            catch (WorkspaceEscapeException)
            {
                return Fail($"Refused: '{file.RelativePath}' escapes the run boundary.", applied);
            }

            var currentHash = fileAccess.FileHash(target);
            if (!string.Equals(currentHash ?? string.Empty, file.BeforeHash ?? string.Empty, StringComparison.Ordinal))
            {
                return Fail($"Refused: '{file.RelativePath}' changed since the proposal; apply the remaining plan again.", applied);
            }

            if (file.IsBinary || file.ChangeType == FileChangeType.Deleted)
            {
                if (file.ChangeType == FileChangeType.Deleted && !fileAccess.FileExists(target))
                {
                    return Fail($"Refused: '{file.RelativePath}' was expected to exist for deletion.", applied);
                }

                if (file.ChangeType == FileChangeType.Deleted)
                {
                    fileAccess.DeleteFile(target);
                }
                else
                {
                    return Fail($"Refused: binary content cannot be applied in this MVP ('{file.RelativePath}').", applied);
                }
            }
            else
            {
                fileAccess.WriteText(target, file.AfterContentText!);
            }

            applied.Add(file.RelativePath);
        }

        var summary = applied.Count == changeSet.Files.Count
            ? $"Applied {applied.Count} file(s) at {clock.UtcNow:O}."
            : $"Partially applied {applied.Count}/{changeSet.Files.Count} file(s).";

        return new PatchApplicationResult(
            AllApplied: applied.Count == changeSet.Files.Count && changeSet.Files.Count > 0,
            summary,
            applied);

        static PatchApplicationResult Fail(string message, List<string> alreadyApplied) =>
            new(AllApplied: false, message, alreadyApplied);
    }
}
