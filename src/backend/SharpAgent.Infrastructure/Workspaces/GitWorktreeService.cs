using System.Diagnostics;
using SharpAgent.Application.Abstractions;

namespace SharpAgent.Infrastructure.Workspaces;

/// <summary>
/// Disposable Git worktrees per run (FR-034). The registered base checkout is only
/// ever read as a worktree SOURCE — patches apply inside the worktree copy.
/// </summary>
public sealed class GitWorktreeService : IGitWorktreeService
{
    public bool Exists(string worktreePath) =>
        !string.IsNullOrWhiteSpace(worktreePath) && Directory.Exists(worktreePath);

    public Task<WorktreeInfo> CreateAsync(string baseRepositoryRoot, string runId, CancellationToken cancellationToken)
    {
        var environmentId = $"wt_{Sha(runId + baseRepositoryRoot)}";
        var parent = Path.Combine(Path.GetTempPath(), "sharpagent-worktrees");
        Directory.CreateDirectory(parent);
        var worktreePath = Path.Combine(parent, environmentId);

        if (!Directory.Exists(worktreePath))
        {
            // Requires at least one commit in the source repository.
            Git(baseRepositoryRoot, ["worktree", "add", "--detach", worktreePath], TimeSpan.FromSeconds(60));
        }

        return Task.FromResult(new WorktreeInfo(environmentId, worktreePath));
    }

    public Task RemoveAsync(WorktreeInfo worktree, CancellationToken cancellationToken)
    {
        if (Directory.Exists(worktree.Path))
        {
            // Detached worktrees have no branch to clean; --force discards contents.
            try
            {
                Git(worktree.Path, ["worktree", "remove", "--force", worktree.Path], TimeSpan.FromSeconds(60));
            }
            catch (ProcessFailedException)
            {
                // Fall back to direct deletion when the metadata is already gone.
                Directory.Delete(worktree.Path, recursive: true);
            }
        }

        return Task.CompletedTask;
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
            throw new ProcessFailedException($"git failed with exit code {process.ExitCode}.");
        }
    }

    private static string Sha(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text)))[..16];

    private sealed class ProcessFailedException(string message) : Exception(message);
}

