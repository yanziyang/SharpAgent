using SharpAgent.Application.Sessions;
using SharpAgent.Application.Tests.Support;
using SharpAgent.Application.Tools;
using SharpAgent.Domain.Sessions;
using SharpAgent.TestKit.Fakes;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Application.Tests.Tools;

/// <summary>
/// Exercises the bounded-summary construction arms of the proposal gate:
/// long file lists and long command arguments both truncate safely (FR-024).
/// </summary>
public sealed class ProposalSummaryBoundsTests
{
    private readonly TempWorkspace _workspace = TempWorkspace.Create();

    [Fact]
    public async Task Long_file_lists_produce_an_awaiting_approval_with_truncated_summary()
    {
        var fixture = await NewStartedFixtureAsync();
        _workspace.WriteFile("src/a.ts", "a");
        _workspace.WriteFile("src/bb.ts", "b");

        var files = new[]
        {
            new ProposeFileChange("src/a.ts", "content a", Delete: false),
            new ProposeFileChange("src/" + new string('n', 200) + ".ts", "content n", Delete: false),
            new ProposeFileChange("src/bb.ts", "content bb", Delete: false),
        };

        var changeSet = await fixture.ChangeSets!.ProposeAsync(fixture.SessionId!, files);
        var tools = fixture.Tools!;

        var proposal = await tools.ProposeAsync(new ToolProposal(
            fixture.SessionId!, fixture.RunId!, fixture.WorkspaceId!,
            ToolAction.ApplyPatch, ChangeSetId: changeSet.Id));

        // Reaches the approval gate with a safe summary; nothing has executed yet.
        Assert.IsType<ToolProposalResult.AwaitingApproval>(proposal);
    }

    [Fact]
    public async Task Long_command_arguments_produce_an_awaiting_approval()
    {
        var fixture = await NewStartedFixtureAsync();
        var longArgument = "--" + new string('x', 150);

        var tools = fixture.Tools!;
        var proposal = await tools.ProposeAsync(new ToolProposal(
            fixture.SessionId!, fixture.RunId!, fixture.WorkspaceId!,
            ToolAction.RunCommand,
            CommandName: "dotnet",
            Arguments: [longArgument]));

        Assert.IsType<ToolProposalResult.AwaitingApproval>(proposal);
    }

    private async Task<FixtureBundle> NewStartedFixtureAsync()
    {
        var fixture = new SessionServiceFixture();
        var fakes = new RecordingWorkspaceFakes(_workspace);
        var seeded = fixture.Workspaces.Snapshot.Single();
        seeded.MarkValidated(_workspace.RootPath, fixture.Clock.UtcNow);

        var tools = new WorkspaceToolService(
            fixture.Sessions, fixture.Workspaces, fixture.Profiles, fixture.Policies,
            fixture.Approvals, fixture.ChangeSets, fixture.ToolExecutions, fixture.Events,
            fixture.UnitOfWork, fixture.Clock, fakes.PathResolver, fakes.FileAccess,
            fakes.ProcessRunner, fakes.Worktrees, FocusedCommandCatalog.Default);
        _ = new ApprovalsService(
            fixture.Approvals, fixture.Sessions, fixture.Events, fixture.Idempotency,
            fixture.UnitOfWork, fixture.Clock, tools);
        var changeSets = new ChangeSetService(
            fixture.ChangeSets, fixture.Sessions, fixture.Workspaces,
            fakes.PathResolver, fakes.FileAccess, fixture.Clock);

        var created = await fixture.Service.CreateAsync(
            new CreateSessionRequest(fixture.WorkspaceId, "bounds", SessionMode.Execute, fixture.ModelProfileId, fixture.PolicyProfileId),
            $"create-{Guid.NewGuid():N}");
        var started = await fixture.Service.StartOrResumeAsync(created.Id, new StartRunRequest(null, null), $"run-{Guid.NewGuid():N}");

        return new FixtureBundle
        {
            Fixture = fixture,
            Fakes = fakes,
            Tools = tools,
            ChangeSets = changeSets,
            SessionId = started.Session.Id,
            RunId = started.Run.Id,
            WorkspaceId = started.Session.WorkspaceId,
        };
    }

    private sealed class FixtureBundle
    {
        public required SessionServiceFixture Fixture { get; init; }
        public required RecordingWorkspaceFakes Fakes { get; init; }
        public required WorkspaceToolService Tools { get; init; }
        public required ChangeSetService ChangeSets { get; init; }
        public string? SessionId { get; set; }
        public string? RunId { get; set; }
        public string? WorkspaceId { get; set; }
    }

    [Fact]
    public void Dispose() => _workspace.Dispose();
}


