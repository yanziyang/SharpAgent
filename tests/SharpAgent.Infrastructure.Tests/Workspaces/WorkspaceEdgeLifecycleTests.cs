using System.Diagnostics;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Tools;
using SharpAgent.Infrastructure.Workspaces;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Infrastructure.Tests.Workspaces;

/// <summary>
/// Direct behavioral coverage for the workspace edges used by every controlled
/// tool execution: bounded file access and disposable git worktrees.
/// </summary>
public sealed class WorkspaceEdgeLifecycleTests : IDisposable
{
    private readonly TempWorkspace _workspace = TempWorkspace.Create();

    private readonly BoundedFileAccess _files = new();

    [Fact]
    public void Read_is_bounded_and_flags_truncation()
    {
        _workspace.WriteFile("big.txt", new string('a', 500));

        var (full, fullTruncated) = _files.ReadTextBounded(_resolver("big.txt"), 1_000);
        Assert.Equal(500, full.Length);
        Assert.False(fullTruncated);

        var (partial, partialTruncated) = _files.ReadTextBounded(_resolver("big.txt"), 100);
        Assert.Equal(100, partial.Length);
        Assert.True(partialTruncated);
    }

    [Fact]
    public void Write_creates_parent_directories_and_delete_removes_files()
    {
        var target = _resolver("deep/nested/file.txt");

        Assert.False(_files.FileExists(target));
        _files.WriteText(target, "created");
        Assert.True(_files.FileExists(target));
        Assert.True(_files.DirectoryExists(_resolver("deep/nested")));

        _files.DeleteFile(target);
        Assert.False(_files.FileExists(target));
    }

    [Fact]
    public void ListTopLevel_separates_directories_from_files()
    {
        _workspace.WriteFile("child.txt", "x");
        Directory.CreateDirectory(Path.Combine(_workspace.RootPath, "folder"));

        var entries = _files.ListTopLevel(_resolver("."));

        Assert.Contains(entries, static entry => entry.IsDirectory && entry.Name == "folder");
        Assert.Contains(entries, static entry => !entry.IsDirectory && entry.Name == "child.txt");
    }

    [Fact]
    public void Search_returns_bounded_line_matches()
    {
        _workspace.WriteFile("hay.txt", "needle one\nnothing\nneedle two");

        var matches = _files.SearchText(_resolver("."), "needle", 10, out var truncated);

        Assert.Equal(2, matches.Count);
        Assert.All(matches, static match => match.Contains("needle", StringComparison.Ordinal));
        Assert.False(truncated);
    }

    [Fact]
    public void Search_skips_binary_files_and_finds_matches_past_the_sniff_window()
    {
        _workspace.WriteFile("text.txt", new string('a', 2_000) + "\nneedle at the end");
        File.WriteAllBytes(
            Path.Combine(_workspace.RootPath, "blob.bin"),
            [.. Enumerable.Repeat((byte)0, 64)]);

        var matches = _files.SearchText(_resolver("."), "needle", 10, out var truncated);

        var match = Assert.Single(matches);
        Assert.Contains("text.txt", match, StringComparison.Ordinal);
        Assert.False(truncated);
    }

    [Fact]
    public void Search_bounds_results_and_flags_truncation()
    {
        _workspace.WriteFile("many.txt", string.Join('\n', Enumerable.Repeat("needle hit", 25)));

        var matches = _files.SearchText(_resolver("."), "needle", 5, out var truncated);

        Assert.Equal(5, matches.Count);
        Assert.True(truncated);
    }

    [Fact]
    public void Search_skips_files_that_cannot_be_opened()
    {
        _workspace.WriteFile("locked.txt", "needle locked");
        using var exclusive = new FileStream(
            Path.Combine(_workspace.RootPath, "locked.txt"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var matches = _files.SearchText(_resolver("."), "needle", 10, out _);

        Assert.DoesNotContain(matches, static match => match.Contains("locked.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void FileHash_reflects_content_changes()
    {
        _workspace.WriteFile("hash.txt", "v1");
        var before = _files.FileHash(_resolver("hash.txt"));

        _workspace.WriteFile("hash.txt", "v2");
        var after = _files.FileHash(_resolver("hash.txt"));

        Assert.NotEqual(before, after);
        Assert.Null(_files.FileHash(_resolver("missing.txt")));
    }

    [Fact]
    public async Task Worktree_lifecycle_creates_isolated_copies_and_removes_them()
    {
        _workspace.WriteFile("src/base.cs", "// base");
        Git(_workspace.RootPath, ["init", "-b", "main"]);
        Git(_workspace.RootPath, ["add", "."]);
        Git(_workspace.RootPath, ["-c", "user.name=t", "-c", "user.email=t@local", "commit", "-m", "init"]);

        var service = new GitWorktreeService();
        var info = await service.CreateAsync(_workspace.RootPath, "run_abc", CancellationToken.None);

        Assert.True(Directory.Exists(info.Path));
        Assert.NotEqual(_workspace.RootPath, info.Path);

        // Writes inside the worktree never touch the base checkout.
        await File.WriteAllTextAsync(Path.Combine(info.Path, "src", "base.cs"), "// patched");
        Assert.Contains("// base", await File.ReadAllTextAsync(Path.Combine(_workspace.RootPath, "src", "base.cs")));

        Assert.True(service.Exists(info.Path));
        await service.RemoveAsync(info, CancellationToken.None);
        Assert.False(service.Exists(info.Path));

        // Removing twice is a safe no-op.
        await service.RemoveAsync(info, CancellationToken.None);
    }

    private ResolvedTarget _resolver(string relative)
    {
        var root = Path.GetFullPath(_workspace.RootPath);
        return new ResolvedTarget(Path.Combine(root, relative), relative);
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
