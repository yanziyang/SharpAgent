using SharpAgent.Application.Tools;

namespace SharpAgent.Application.Abstractions;

/// <summary>
/// Resolves and re-canonicalizes every proposed target immediately before execution
/// (FR-002, FR-036). Implementations must reject traversal, foreign absolute paths,
/// missing roots, and links escaping the registered root.
/// </summary>
public interface IWorkspacePathResolver
{
    /// <exception cref="WorkspaceEscapeException">The target escapes the boundary.</exception>
    ResolvedTarget Resolve(string workspaceCanonicalRoot, string relativePath);
}

/// <summary>
/// Bounded file access inside one resolved workspace root. Every method receives an
/// already-resolved absolute path; implementations must still verify containment.
/// </summary>
public interface IWorkspaceFileAccess
{
    bool FileExists(ResolvedTarget target);

    bool DirectoryExists(ResolvedTarget target);

    /// <summary>Bounded UTF-8 text read; truncated flag set when the cap was hit.</summary>
    (string Content, bool Truncated) ReadTextBounded(ResolvedTarget target, int maxCharacters);

    void WriteText(ResolvedTarget target, string contents);

    void DeleteFile(ResolvedTarget target);

    IReadOnlyList<(string Name, long Length, bool IsDirectory)> ListTopLevel(ResolvedTarget directory);

    /// <summary>Non-recursive text search with bounded result count.</summary>
    IReadOnlyList<string> SearchText(
        ResolvedTarget directory,
        string query,
        int maxResults,
        out bool resultsTruncated);

    /// <summary>Recursive bounded text search that skips reparse points.</summary>
    IReadOnlyList<string> SearchTextRecursive(
        ResolvedTarget directory,
        string query,
        int maxResults,
        out bool resultsTruncated);

    /// <summary>Bounded file-name search that skips reparse points.</summary>
    IReadOnlyList<string> FindFiles(
        ResolvedTarget directory,
        string namePattern,
        int maxResults,
        out bool resultsTruncated);

    /// <summary>SHA-256 hex of current file bytes; null when the file does not exist.</summary>
    string? FileHash(ResolvedTarget target);
}

public sealed record ProcessExecutionRequest(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TimeSpan Timeout,
    int OutputLimitCharacters,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null);

public sealed record ProcessExecutionResult(
    int? ExitCode,
    string CombinedOutput,
    bool OutputTruncated,
    bool TimedOut,
    bool Cancelled)
{
    public bool Succeeded => !TimedOut && !Cancelled && ExitCode == 0;
}

/// <summary>
/// Hardened process runner: no shell, fixed working directory, timeout,
/// kill-process-tree cancellation, output caps, environment allowlist.
/// </summary>
public interface IProcessRunner
{
    ProcessExecutionResult Run(ProcessExecutionRequest request, CancellationToken cancellationToken);
}

public sealed record WorktreeInfo(string EnvironmentId, string Path);

/// <summary>Disposable per-run worktrees. The registered base checkout is never a patch target.</summary>
public interface IGitWorktreeService
{
    /// <summary>True when the given path is a live worktree directory.</summary>
    bool Exists(string worktreePath);

    Task<WorktreeInfo> CreateAsync(string baseRepositoryRoot, string runId, CancellationToken cancellationToken);

    Task RemoveAsync(WorktreeInfo worktree, CancellationToken cancellationToken);
}
