using System.Diagnostics;
using SharpAgent.Application.Abstractions;

namespace SharpAgent.Infrastructure.Workspaces;

/// <summary>
/// Disposable Git worktrees per run (FR-034). The registered base checkout is only
/// ever read as a worktree SOURCE — patches apply inside the worktree copy.
/// </summary>
public sealed class GitWorktreeService : IGitWorktreeService
{
    // One-service-process deployment (design section 4.3): caching the base root
    // here lets removal run git against the MAIN repo even while deleting a child.
    private string? lastKnownBaseRoot;

    public bool Exists(string worktreePath) =>
        !string.IsNullOrWhiteSpace(worktreePath) && Directory.Exists(worktreePath);

    public Task<WorktreeInfo> CreateAsync(string baseRepositoryRoot, string runId, CancellationToken cancellationToken)
    {
        var environmentId = $"wt_{Sha(runId + baseRepositoryRoot)}";
        var parent = Path.Combine(Path.GetTempPath(), "sharpagent-worktrees");
        Directory.CreateDirectory(parent);
        var worktreePath = Path.Combine(parent, environmentId);
        lastKnownBaseRoot = baseRepositoryRoot;

        if (!Directory.Exists(worktreePath))
        {
            // Requires at least one commit in the source repository.
            Git(baseRepositoryRoot, ["worktree", "add", "--detach", worktreePath], TimeSpan.FromSeconds(60));
        }

        return Task.FromResult(new WorktreeInfo(environmentId, worktreePath));
    }

    public Task RemoveAsync(WorktreeInfo worktree, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(worktree.Path))
        {
            return Task.CompletedTask;
        }

        try
        {
            // Must run from the MAIN repository; running from inside the worktree
            // being removed makes git refuse.
            var mainRepo = lastKnownBaseRoot
                           ?? FindMainRepository(worktree.Path)
                           ?? throw new ProcessFailedException("Main repository root unknown; cannot remove worktree.");
            Git(mainRepo, ["worktree", "remove", "--force", worktree.Path], TimeSpan.FromSeconds(60));
        }
        catch (ProcessFailedException)
        {
            TryForceDelete(worktree.Path);
        }
        catch (InvalidOperationException)
        {
            TryForceDelete(worktree.Path);
        }

        return Task.CompletedTask;
    }

    /// <summary>Git marks object files read-only; strip attributes before recursive delete.</summary>
    private static void TryForceDelete(string path)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Retention sweeps handle leftovers later.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string? FindMainRepository(string worktreePath)
    {
        var dotGit = Path.Combine(worktreePath, ".git");
        if (!File.Exists(dotGit))
        {
            return null;
        }

        // Worktree .git files read like: "gitdir: <main>/.git/worktrees/<name>"
        var line = File.ReadAllText(dotGit);
        var marker = "gitdir:";
        if (!line.StartsWith(marker, StringComparison.Ordinal))
        {
            return null;
        }

        var gitDir = line[marker.Length..].Trim();
        var worktreesIndex = gitDir.IndexOf($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}worktrees", StringComparison.OrdinalIgnoreCase);
        return worktreesIndex < 0 ? null : gitDir[..worktreesIndex];
    }

    private static void Git(string workingDirectory, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new ProcessFailedException("git could not be started.");

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new ProcessFailedException("git timed out.");
        }

        Task.WaitAll([output, error], (int)timeout.TotalMilliseconds);

        if (process.ExitCode != 0)
        {
            throw new ProcessFailedException($"git failed with exit code {process.ExitCode}: {error.Result.Trim()}");
        }
    }

    private static string Sha(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text)))[..16];

    private sealed class ProcessFailedException(string message) : Exception(message);
}


