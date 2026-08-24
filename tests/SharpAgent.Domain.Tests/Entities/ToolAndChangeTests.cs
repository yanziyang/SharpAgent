using SharpAgent.Domain.Changes;
using SharpAgent.Domain.Common;
using SharpAgent.Domain.Tools;
using Xunit;

namespace SharpAgent.Domain.Tests.Entities;

public sealed class ToolExecutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Start_requires_tool_name()
    {
        Assert.Throws<ArgumentException>(
            () => ToolExecution.Start("run_1", " ", PolicyOutcome.Allow, null, Now));
    }

    [Fact]
    public void Lifecycle_is_single_shot()
    {
        var execution = ToolExecution.Start("run_1", "read_file", PolicyOutcome.Allow, null, Now);

        execution.Complete(0, "...", outputTruncated: true, redactionApplied: false, Now.AddMinutes(1));

        Assert.Equal(ToolExecutionStatus.Completed, execution.Status);
        Assert.True(execution.OutputTruncated);
        Assert.Throws<InvalidStateTransitionException>(() => execution.Fail("late", Now.AddMinutes(2)));
    }

    [Fact]
    public void Failure_records_only_the_safe_summary()
    {
        var execution = ToolExecution.Start(
            "run_1", "run_tests", PolicyOutcome.RequireApproval, "apr_1", Now);

        execution.Fail("Test host exited with code 1.", Now.AddMinutes(5));

        Assert.Equal(ToolExecutionStatus.Failed, execution.Status);
        Assert.NotNull(execution.EndedAtUtc);
    }
}

public sealed class ChangeSetTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Change_sets_collect_files_then_finalize_once()
    {
        var changeSet = ChangeSet.CreateNew("run_1", Now);
        changeSet.AddFile("src/Pricing.tsx", FileChangeType.Modified, Now);
        changeSet.AddFile("src/new.ts", FileChangeType.Added, Now);
        changeSet.AddFile("src/old.ts", FileChangeType.Deleted, Now);

        Assert.Equal(3, changeSet.Files.Count);
        changeSet.MarkApplied("Applied to worktree.", Now.AddMinutes(1));

        Assert.Throws<InvalidStateTransitionException>(
            () => changeSet.MarkFailed("late", Now.AddMinutes(2)));
    }

    [Fact]
    public void Deleted_files_default_to_binary_metadata()
    {
        var changeSet = ChangeSet.CreateNew("run_1", Now);
        var deleted = changeSet.AddFile("a.bin", FileChangeType.Deleted, Now);

        Assert.True(deleted.IsBinary);
    }
}
