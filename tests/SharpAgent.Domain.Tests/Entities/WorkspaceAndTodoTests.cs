using SharpAgent.Domain.Common;
using SharpAgent.Domain.Todos;
using SharpAgent.Domain.Workspaces;
using Xunit;

namespace SharpAgent.Domain.Tests.Entities;

public sealed class WorkspaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Register_requires_name_and_root()
    {
        Assert.Throws<ArgumentException>(() => Workspace.Register(string.Empty, @"C:\repos\demo", Now));
        Assert.Throws<ArgumentException>(() => Workspace.Register("Demo", " ", Now));
    }

    [Fact]
    public void Validation_lifecycle_records_safe_details_only()
    {
        var workspace = Workspace.Register("Demo", @"C:\repos\demo", Now);

        Assert.Equal(WorkspaceStatus.PendingValidation, workspace.Status);

        workspace.MarkValidated(@"C:\repos\demo", Now.AddMinutes(1));
        Assert.Equal(WorkspaceStatus.Available, workspace.Status);
        Assert.Equal(@"C:\repos\demo", workspace.CanonicalRootPath);

        workspace.MarkUnavailable("Root directory is missing.", Now.AddMinutes(2));
        Assert.Equal(WorkspaceStatus.Unavailable, workspace.Status);
        Assert.Equal("Root directory is missing.", workspace.ValidationMessage);

        workspace.MarkValidationFailed("Root is not an absolute path.", Now.AddMinutes(3));
        Assert.Equal(WorkspaceStatus.ValidationFailed, workspace.Status);
    }
}

public sealed class TodoItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_validates_sequence_and_text()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TodoItem.Create("ses", "run", 0, "step", Now));
        Assert.Throws<ArgumentException>(
            () => TodoItem.Create("ses", "run", 1, string.Empty, Now));
    }

    [Fact]
    public void Todos_follow_the_visible_plan_flow_including_reopen()
    {
        var todo = TodoItem.Create("ses", "run", 1, "Read pricing module", Now);

        todo.TransitionTo(TodoStatus.InProgress, Now.AddMinutes(1));
        todo.UpdateText("Read pricing module and tests", Now.AddMinutes(2));
        todo.TransitionTo(TodoStatus.Completed, Now.AddMinutes(3));

        // Replanning may re-open completed items.
        todo.TransitionTo(TodoStatus.Pending, Now.AddMinutes(4));
        Assert.Equal(TodoStatus.Pending, todo.Status);
        Assert.Equal("Read pricing module and tests", todo.Text);
    }
}
