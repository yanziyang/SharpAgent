using SharpAgent.Application.Abstractions;
using SharpAgent.Infrastructure.Workspaces;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Infrastructure.Tests.Workspaces;

/// <summary>AC-07: every escape vector is refused BEFORE any filesystem action.</summary>
public sealed class CanonicalPathResolverTests : IDisposable
{
    private readonly TempWorkspace _workspace = TempWorkspace.Create();

    private readonly CanonicalPathResolver _resolver = new();

    [Fact]
    public void Parent_traversal_is_rejected()
    {
        Assert.Throws<SharpAgent.Application.Tools.WorkspaceEscapeException>(
            () => _resolver.Resolve(_workspace.RootPath, "../../escape.txt"));
        Assert.Throws<SharpAgent.Application.Tools.WorkspaceEscapeException>(
            () => _resolver.Resolve(_workspace.RootPath, "src/../../../escape.txt"));
    }

    [Fact]
    public void Foreign_absolute_paths_are_rejected()
    {
        var foreignRoot = Path.GetTempPath();
        if (_workspace.RootPath.StartsWith(foreignRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Same drive/root prefix; use an unrelated absolute path instead.
            foreignRoot = @"C:\Windows\System32\drivers\etc\hosts";
        }
        else
        {
            foreignRoot = Path.Combine(foreignRoot, "elsewhere.txt");
        }

        Assert.Throws<SharpAgent.Application.Tools.WorkspaceEscapeException>(
            () => _resolver.Resolve(_workspace.RootPath, foreignRoot));
    }

    [Fact]
    public void Blank_roots_and_paths_are_rejected()
    {
        Assert.Throws<SharpAgent.Application.Tools.WorkspaceEscapeException>(
            () => _resolver.Resolve(string.Empty, "a.txt"));
        Assert.Throws<SharpAgent.Application.Tools.WorkspaceEscapeException>(
            () => _resolver.Resolve(_workspace.RootPath, "  "));
    }

    [Fact]
    public void In_boundary_relative_paths_resolve_with_relative_form()
    {
        _workspace.WriteFile("src/lib/util.cs", "// sample");

        var resolved = _resolver.Resolve(_workspace.RootPath, "src/lib/util.cs");

        Assert.Equal("src\\lib\\util.cs", resolved.RelativePath);
        Assert.StartsWith(
            _workspace.RootPath,
            resolved.AbsolutePath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Junctions_pointing_outside_the_root_are_rejected_before_use()
    {
        // Directory junctions can be created WITHOUT admin rights on Windows.
        var outside = Path.Combine(Path.GetTempPath(), "sharpagent-outside-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "leak.txt"), "secret");
            var junction = Path.Combine(_workspace.RootPath, "link");
            RunCmd($"/c mklink /J \"{junction}\" \"{outside}\"");

            Assert.Throws<SharpAgent.Application.Tools.WorkspaceEscapeException>(
                () => _resolver.Resolve(_workspace.RootPath, "link/leak.txt"));
        }
        finally
        {
            TryDelete(outside);
        }
    }

    [Fact]
    public void File_links_resolving_inside_the_root_are_allowed()
    {
        // File symlinks require either admin rights or Windows developer mode; if
        // the environment cannot create one the scenario is simply skipped.
        var real = Path.Combine(_workspace.RootPath, "real.txt");
        File.WriteAllText(real, "value");
        var alias = Path.Combine(_workspace.RootPath, "alias.txt");
        if (!TryCreateFileLink(alias, real))
        {
            return;
        }

        var resolved = _resolver.Resolve(_workspace.RootPath, "alias.txt");

        Assert.Equal("alias.txt", resolved.RelativePath);
        Assert.Equal(real, resolved.AbsolutePath);
    }

    private static bool TryCreateFileLink(string linkPath, string targetPath)
    {
        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink \"{linkPath}\" \"{targetPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });

        if (process is null || !process.WaitForExit(10_000))
        {
            return false;
        }

        return process.ExitCode == 0;
    }

    private static void RunCmd(string arguments)
    {
        using var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("cmd.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });

        Assert.NotNull(process);
        Assert.True(process!.WaitForExit(10_000), "mklink did not finish in time.");
        Assert.Equal(0, process.ExitCode);
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    public void Dispose() => _workspace.Dispose();
}

public sealed class HardenedProcessRunnerTests
{
    private readonly HardenedProcessRunner _runner = new();

    private static ProcessExecutionRequest Cmd(string arguments, TimeSpan? timeout = null, int limit = 4_000) =>
        new("cmd.exe", ["/c", arguments], AppContext.BaseDirectory, timeout ?? TimeSpan.FromSeconds(15), limit);

    [Fact]
    public void Successful_commands_capture_output_and_exit_code()
    {
        var result = _runner.Run(Cmd("echo sharpagent-ok"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("sharpagent-ok", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Timeouts_kill_the_process_tree()
    {
        // ping -n 30 sleeps ~30s; a 1-second timeout must terminate it.
        var result = _runner.Run(Cmd("ping -n 30 127.0.0.1 > nul", TimeSpan.FromSeconds(1)), CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Cancellation_kills_the_running_process()
    {
        using var cts = new CancellationTokenSource(500);
        var result = _runner.Run(Cmd("ping -n 30 127.0.0.1 > nul"), cts.Token);

        Assert.True(result.Cancelled || result.TimedOut);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Oversized_output_is_truncated_with_the_flag_set()
    {
        var result = _runner.Run(Cmd("for /l %i in (1,1,2000) do @echo 01234567890123456789"), CancellationToken.None);

        Assert.True(result.OutputTruncated);
        Assert.True(result.CombinedOutput.Length <= 4_100); // small slack for the marker
        Assert.EndsWith("[output truncated]", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Nonzero_exit_codes_are_preserved()
    {
        var result = _runner.Run(Cmd("exit /b 7"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public void Environment_is_stripped_to_the_allowlist()
    {
        Environment.SetEnvironmentVariable("SHARPAGENT_CANARY", "must-not-leak");

        var result = _runner.Run(Cmd("echo %SHARPAGENT_CANARY%"), CancellationToken.None);

        Assert.DoesNotContain("must-not-leak", result.CombinedOutput, StringComparison.Ordinal);
        Environment.SetEnvironmentVariable("SHARPAGENT_CANARY", null);
    }

    [Fact]
    public void Requested_environment_variables_are_passed_to_the_child()
    {
        var result = _runner.Run(new ProcessExecutionRequest(
            "cmd.exe",
            ["/c", "echo %SHARPAGENT_REQUESTED%"],
            AppContext.BaseDirectory,
            TimeSpan.FromSeconds(15),
            4_000,
            new Dictionary<string, string> { ["SHARPAGENT_REQUESTED"] = "canary-value" }),
            CancellationToken.None);

        Assert.Contains("canary-value", result.CombinedOutput, StringComparison.Ordinal);
    }
}
