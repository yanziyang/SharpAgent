using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Application.Tests.TestKit;

public sealed class TempWorkspaceTests
{
    [Fact]
    public void Creates_fixture_copy_with_expected_files()
    {
        using var workspace = TempWorkspace.Create();

        Assert.True(Directory.Exists(workspace.RootPath));
        Assert.NotEqual(AppContext.BaseDirectory, workspace.RootPath);
        Assert.True(File.Exists(Path.Combine(workspace.RootPath, "README.md")));
        Assert.True(File.Exists(Path.Combine(workspace.RootPath, "src", "lib", "util.cs")));
    }

    [Fact]
    public async Task Fixture_contents_are_deterministic_between_instances()
    {
        using var first = TempWorkspace.Create();
        using var second = TempWorkspace.Create();

        Assert.Equal(
            await File.ReadAllTextAsync(Path.Combine(first.RootPath, "src", "app.cs")),
            await File.ReadAllTextAsync(Path.Combine(second.RootPath, "src", "app.cs")));
    }

    [Fact]
    public void WriteFile_creates_nested_file_inside_root()
    {
        using var workspace = TempWorkspace.Create();

        var path = workspace.WriteFile("docs/notes/readme.txt", "hello");

        Assert.StartsWith(workspace.RootPath, path, StringComparison.Ordinal);
        Assert.Equal("hello", File.ReadAllText(path));
    }

    [Fact]
    public void WriteFile_rejects_paths_outside_the_root()
    {
        using var workspace = TempWorkspace.Create();

        Assert.Throws<ArgumentException>(() => workspace.WriteFile("../escape.txt", "no"));
        Assert.Throws<ArgumentException>(() => workspace.WriteFile("/abs/path.txt", "no"));
        Assert.Throws<ArgumentException>(() => workspace.WriteFile("a|b.txt", "no"));
    }

    [Fact]
    public void Dispose_removes_the_temporary_tree()
    {
        var workspace = TempWorkspace.Create();
        var root = workspace.RootPath;

        workspace.Dispose();

        Assert.False(Directory.Exists(root));
        workspace.Dispose(); // Idempotent.
    }
}
