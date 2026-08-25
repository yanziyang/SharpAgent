using SharpAgent.TestKit.Workspaces;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Tools;

namespace SharpAgent.TestKit.Fakes;

/// <summary>
/// Real-filesystem workspace fakes rooted in a temp directory. They RECORD every
/// executor call so tests can prove that guarded proposals never reach them.
/// </summary>
public sealed class RecordingWorkspaceFakes : IDisposable
{
    public TempWorkspace Directory { get; }

    public RecordingPathResolver PathResolver { get; } = new();

    public RecordingFileAccess FileAccess { get; }

    public RecordingProcessRunner ProcessRunner { get; } = new();

    public RecordingWorktreeService Worktrees { get; }

    /// <summary>Executor invocations across file/process/worktree edges.</summary>
    public int ExecutorCalls => FileAccess.CallCount + ProcessRunner.CallCount;

    public void Dispose() => Directory.Dispose();

    public RecordingWorkspaceFakes(TempWorkspace directory)
    {
        Directory = directory;
        FileAccess = new RecordingFileAccess(directory.RootPath);
        Worktrees = new RecordingWorktreeService(directory.RootPath);
    }
}

/// <summary>Resolver with real path math but no link resolution (link tests live in Infrastructure).</summary>
public sealed class RecordingPathResolver : IWorkspacePathResolver
{
    public int CallCount { get; private set; }

    public ResolvedTarget Resolve(string workspaceCanonicalRoot, string relativePath)
    {
        CallCount++;
        var root = Path.GetFullPath(workspaceCanonicalRoot);
        var candidate = Path.GetFullPath(
            Path.IsPathFullyQualified(relativePath) ? relativePath : Path.Combine(root, relativePath));

        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var isRootItself = string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase);
        if (!isRootItself && !candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceEscapeException($"'{relativePath}' escapes the workspace.");
        }

        return new ResolvedTarget(candidate, Path.GetRelativePath(root, candidate));
    }
}

public sealed class RecordingFileAccess : IWorkspaceFileAccess
{
    private readonly string _root;

    public RecordingFileAccess(string root) => _root = root;

    public int CallCount { get; private set; }

    private void Count() => CallCount++;

    public bool FileExists(ResolvedTarget target)
    {
        Count();
        return File.Exists(target.AbsolutePath);
    }

    public bool DirectoryExists(ResolvedTarget target)
    {
        Count();
        return System.IO.Directory.Exists(target.AbsolutePath);
    }

    public (string Content, bool Truncated) ReadTextBounded(ResolvedTarget target, int maxCharacters)
    {
        Count();
        var text = File.ReadAllText(target.AbsolutePath);
        return text.Length <= maxCharacters ? (text, false) : (text[..maxCharacters], true);
    }

    public void WriteText(ResolvedTarget target, string contents)
    {
        Count();
        var fullPath = target.AbsolutePath;
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, contents);
    }

    public void DeleteFile(ResolvedTarget target)
    {
        Count();
        if (File.Exists(target.AbsolutePath))
        {
            File.Delete(target.AbsolutePath);
        }
    }

    public IReadOnlyList<(string Name, long Length, bool IsDirectory)> ListTopLevel(ResolvedTarget directory)
    {
        Count();
        var list = new List<(string, long, bool)>();
        foreach (var dir in System.IO.Directory.EnumerateDirectories(directory.AbsolutePath))
        {
            list.Add((Path.GetFileName(dir), 0, true));
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(directory.AbsolutePath))
        {
            list.Add((Path.GetFileName(file), new FileInfo(file).Length, false));
        }

        return list;
    }

    public IReadOnlyList<string> SearchText(
        ResolvedTarget directory,
        string query,
        int maxResults,
        out bool resultsTruncated)
    {
        Count();
        resultsTruncated = false;
        var matches = new List<string>();
        foreach (var file in System.IO.Directory.EnumerateFiles(directory.AbsolutePath))
        {
            foreach (var (line, index) in File.ReadLines(file).Select(static (text, i) => (text, i)))
            {
                if (matches.Count >= maxResults)
                {
                    resultsTruncated = true;
                    return matches;
                }

                if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add($"{Path.GetFileName(file)}:{index + 1}: {line.Trim()}");
                }
            }
        }

        return matches;
    }

    public IReadOnlyList<string> SearchTextRecursive(
        ResolvedTarget directory,
        string query,
        int maxResults,
        out bool resultsTruncated)
    {
        Count();
        resultsTruncated = false;
        var matches = new List<string>();
        foreach (var file in System.IO.Directory.EnumerateFiles(directory.AbsolutePath, "*", SearchOption.AllDirectories))
        {
            foreach (var (line, index) in File.ReadLines(file).Select(static (text, i) => (text, i)))
            {
                if (matches.Count >= maxResults)
                {
                    resultsTruncated = true;
                    return matches;
                }

                if (line.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = Path.GetRelativePath(directory.AbsolutePath, file).Replace(Path.DirectorySeparatorChar, '/');
                    matches.Add($"{relative}:{index + 1}: {line.Trim()}");
                }
            }
        }

        return matches;
    }

    public IReadOnlyList<string> FindFiles(
        ResolvedTarget directory,
        string namePattern,
        int maxResults,
        out bool resultsTruncated)
    {
        Count();
        resultsTruncated = false;
        var matches = new List<string>();
        foreach (var file in System.IO.Directory.EnumerateFiles(directory.AbsolutePath, namePattern, SearchOption.AllDirectories))
        {
            if (matches.Count >= maxResults)
            {
                resultsTruncated = true;
                break;
            }

            matches.Add(Path.GetRelativePath(directory.AbsolutePath, file).Replace(Path.DirectorySeparatorChar, '/'));
        }

        return matches;
    }

    public string? FileHash(ResolvedTarget target)
    {
        Count();
        if (!File.Exists(target.AbsolutePath))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(target.AbsolutePath);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}

public sealed class RecordingProcessRunner : IProcessRunner
{
    public int CallCount { get; private set; }

    public List<ProcessExecutionRequest> Requests { get; } = [];

    public Func<ProcessExecutionRequest, ProcessExecutionResult>? Handler { get; set; }

    public ProcessExecutionResult Run(ProcessExecutionRequest request, CancellationToken cancellationToken)
    {
        CallCount++;
        Requests.Add(request);
        return Handler?.Invoke(request)
               ?? new ProcessExecutionResult(0, $"executed:{request.Executable}", false, false, false);
    }
}

public sealed class RecordingWorktreeService(string baseRoot) : IGitWorktreeService
{
    public int CreateCount { get; private set; }

    public string? LastCreatedPath { get; private set; }

    public bool Exists(string worktreePath) =>
        !string.IsNullOrWhiteSpace(worktreePath) && System.IO.Directory.Exists(worktreePath);

    public Task<WorktreeInfo> CreateAsync(string baseRepositoryRoot, string runId, CancellationToken cancellationToken)
    {
        CreateCount++;
        var path = System.IO.Path.Combine(baseRoot, ".sharpagent-worktree-" + runId[..Math.Min(8, runId.Length)]);
        if (!System.IO.Directory.Exists(path))
        {
            System.IO.Directory.CreateDirectory(path);
            CopyDirectory(baseRoot, path); // real worktrees check out the full tree
        }

        LastCreatedPath = path;
        return Task.FromResult(new WorktreeInfo("wt_" + runId[..8].ToLowerInvariant(), path));
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in System.IO.Directory.EnumerateDirectories(source, "*", System.IO.SearchOption.AllDirectories))
        {
            if (directory.Contains(".sharpagent-worktree-", StringComparison.Ordinal))
            {
                continue;
            }

            System.IO.Directory.CreateDirectory(
                System.IO.Path.Combine(destination, System.IO.Path.GetRelativePath(source, directory)));
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(source, "*", System.IO.SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(source, file);
            if (relative.Contains(".sharpagent-worktree-", StringComparison.Ordinal))
            {
                continue;
            }

            File.Copy(file, System.IO.Path.Combine(destination, relative), overwrite: false);
        }
    }

    public Task RemoveAsync(WorktreeInfo worktree, CancellationToken cancellationToken)
    {
        if (System.IO.Directory.Exists(worktree.Path))
        {
            System.IO.Directory.Delete(worktree.Path, true);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Stand-in for environments where tests never exercise worktrees; creation is a
/// hard failure so accidental dependencies surface immediately.
/// </summary>
public sealed class NullWorktreeService : IGitWorktreeService
{
    public bool Exists(string worktreePath) => false;

    public Task<WorktreeInfo> CreateAsync(string baseRepositoryRoot, string runId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("This fixture does not support worktrees.");

    public Task RemoveAsync(WorktreeInfo worktree, CancellationToken cancellationToken) => Task.CompletedTask;
}
