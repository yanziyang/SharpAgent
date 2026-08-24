using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Tools;

namespace SharpAgent.Infrastructure.Workspaces;

/// <summary>
/// Canonicalizes every proposed target immediately before use (FR-002, AC-07):
/// rejects traversal, foreign absolute paths, missing targets for reads and any
/// symlink/junction whose resolved destination escapes the registered root.
/// </summary>
public sealed class CanonicalPathResolver : IWorkspacePathResolver
{
    public ResolvedTarget Resolve(string workspaceCanonicalRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(workspaceCanonicalRoot))
        {
            throw new WorkspaceEscapeException("The workspace root is not available.");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new WorkspaceEscapeException("A target path is required.");
        }

        var root = Path.GetFullPath(workspaceCanonicalRoot);
        var candidate = Path.GetFullPath(Path.IsPathFullyQualified(relativePath)
            ? relativePath
            : Path.Combine(root, relativePath));

        // String-level containment first: cheap and catches "..\.." escapes even when
        // the path does not exist yet (proposed new files).
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceEscapeException($"The target '{relativePath}' is outside the registered workspace.");
        }

        // Link containment second: ANY ancestor between root and target must not be
        // a reparse point whose destination leaves the registered root.
        var relativeSegments = Path.GetRelativePath(root, candidate);
        if (relativeSegments != "." && relativeSegments.Length > 0)
        {
            var accumulated = root;
            foreach (var segment in relativeSegments.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                accumulated = Path.Combine(accumulated, segment);

                if (TryGetLinkDestination(accumulated) is { } linkDestination
                    && !IsInsideRoot(root, linkDestination))
                {
                    throw new WorkspaceEscapeException(
                        $"The target '{relativePath}' resolves through a link outside the workspace.");
                }
            }
        }

        return new ResolvedTarget(candidate, Path.GetRelativePath(root, candidate));
    }

    private static bool IsInsideRoot(string root, string fullPath)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the immediate link destination when the path itself is a link.</summary>
    private static string? TryGetLinkDestination(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Exists && fileInfo.LinkTarget is { } fileLink)
            {
                return Path.GetFullPath(fileLink);
            }

            var dirInfo = new DirectoryInfo(path);
            if (dirInfo.Exists && dirInfo.LinkTarget is { } dirLink)
            {
                return Path.GetFullPath(dirLink);
            }
        }
        catch (IOException)
        {
        }

        return null;
    }
}
