using System.Diagnostics;
using SharpAgent.Infrastructure.Workspaces;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Infrastructure.Tests.Workspaces;

/// <summary>
/// Edge paths of the per-run disposable worktree service: missing commits,
/// repeated create, main-repo discovery via the .git marker, and the
/// force-delete fallback when git itself cannot remove the worktree.
/// </summary>
public sealed class GitWorktreeServiceTests : IDisposable
{
    private readonly TempWorkspace _workspace = TempWorkspace.Create();

    [Fact]
    public async Task Create_requires_at_least_one_commit_in_the_source_repo()
    {
        Git(_workspace.RootPath, ["init", "-b", "main"]);

        var service = new GitWorktreeService();

        await Assert.ThrowsAnyAsync<Exception>(
            () => service.CreateAsync(_workspace.RootPath, "run_no_commits", CancellationToken.None));
    }

    [Fact]
    public async Task Repeated_create_reuses_the_existing_worktree()
    {
        InitializeCommittedRepo();

        var service = new GitWorktreeService();
        var first = await service.CreateAsync(_workspace.RootPath, "run_repeat", CancellationToken.None);
        var second = await service.CreateAsync(_workspace.RootPath, "run_repeat", CancellationToken.None);

        try
        {
            Assert.Equal(first.Path, second.Path);
            Assert.True(service.Exists(second.Path));
        }
        finally
        {
            await service.RemoveAsync(second, CancellationToken.None);
        }
    }

    [Fact]
    public async Task Remove_from_a_fresh_instance_locates_the_main_repository_via_the_marker()
    {
        InitializeCommittedRepo();
        var creator = new GitWorktreeService();
        var info = await creator.CreateAsync(_workspace.RootPath, "run_fresh", CancellationToken.None);

        // A different instance has no cached base root; it must read the worktree's
        // ".git" gitdir marker to find the main repository.
        var fresh = new GitWorktreeService();
        await fresh.RemoveAsync(info, CancellationToken.None);

        Assert.False(creator.Exists(info.Path));
    }

    [Fact]
    public async Task Remove_force_deletes_when_git_remove_fails()
    {
        InitializeCommittedRepo();
        var creator = new GitWorktreeService();
        var info = await creator.CreateAsync(_workspace.RootPath, "run_force", CancellationToken.None);

        // Break the main repository so "git worktree remove" fails and the service
        // must fall back to stripping attributes and deleting the directory.
        ForceDeleteDirectory(Path.Combine(_workspace.RootPath, ".git"));

        var fresh = new GitWorktreeService();
        await fresh.RemoveAsync(info, CancellationToken.None);

        Assert.False(creator.Exists(info.Path));
    }

    [Fact]
    public async Task Remove_force_deletes_when_the_gitdir_marker_is_missing()
    {
        InitializeCommittedRepo();
        var creator = new GitWorktreeService();
        var info = await creator.CreateAsync(_workspace.RootPath, "run_no_marker", CancellationToken.None);

        ForceDeleteFile(Path.Combine(info.Path, ".git"));

        var fresh = new GitWorktreeService();
        await fresh.RemoveAsync(info, CancellationToken.None);

        Assert.False(creator.Exists(info.Path));
    }

    [Fact]
    public async Task Remove_force_deletes_when_the_gitdir_marker_has_no_main_repository()
    {
        InitializeCommittedRepo();
        var creator = new GitWorktreeService();
        var info = await creator.CreateAsync(_workspace.RootPath, "run_bad_marker", CancellationToken.None);

        // A gitdir line that does not point at a "worktrees" folder cannot be traced
        // back to a main repository, so removal must clean up by force delete.
        var marker = Path.Combine(info.Path, ".git");
        File.SetAttributes(marker, FileAttributes.Normal);
        await File.WriteAllTextAsync(marker, "gitdir: C:\\somewhere\\else");

        var fresh = new GitWorktreeService();
        await fresh.RemoveAsync(info, CancellationToken.None);

        Assert.False(creator.Exists(info.Path));
    }

    private static void ForceDeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    private static void ForceDeleteFile(string path)
    {
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    private void InitializeCommittedRepo()
    {
        _workspace.WriteFile("src/base.cs", "// base");
        Git(_workspace.RootPath, ["init", "-b", "main"]);
        Git(_workspace.RootPath, ["add", "."]);
        Git(_workspace.RootPath, ["-c", "user.name=t", "-c", "user.email=t@local", "commit", "-m", "init"]);
    }

    private static void Git(string workingDirectory, IReadOnlyList<string> arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("git missing");

        var errors = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(30_000), "git timed out.");
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git failed: {errors.Result}");
        }
    }

    public void Dispose() => _workspace.Dispose();
}
