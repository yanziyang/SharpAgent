using SharpAgent.Application.Common;
using SharpAgent.Application.Sessions;
using SharpAgent.Application.Tests.Support;
using SharpAgent.Application.Tools;
using SharpAgent.Domain.Changes;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.TestKit.Fakes;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Application.Tests.Tools;

/// <summary>
/// Completes summary-construction, escape-refusal and profile-gating arms for the
/// Phase 2 tool gate (all pure/defensive paths not reachable through happy flows).
/// </summary>
public sealed class Phase2SummaryAndGateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 13, 0, 0, TimeSpan.Zero);

    private readonly TempWorkspace _workspace = TempWorkspace.Create();

    [Theory]
    [InlineData(ToolAction.ReadFile, "src/app.cs", "Read src/app.cs.")]
    [InlineData(ToolAction.ListDirectory, "src", "List directory src.")]
    [InlineData(ToolAction.RepositoryStatus, "", "Show repository working-tree status.")]
    public void Read_only_summaries_describe_the_proposed_target(
        ToolAction action,
        string relativePath,
        string expected)
    {
        var proposal = new ToolProposal(
            "ses", "run", "ws", action, RelativePath: relativePath, SearchQuery: "sum");

        Assert.Equal(expected, WorkspaceToolService.BuildSummary(proposal, []));
    }

    [Fact]
    public void Search_summaries_include_the_bounded_query()
    {
        var proposal = new ToolProposal(
            "ses", "run", "ws", ToolAction.SearchText,
            RelativePath: "src", SearchQuery: "sum");

        Assert.Equal("Search 'sum' in src.", WorkspaceToolService.BuildSummary(proposal, []));
    }

    [Fact]
    public void Patch_summaries_list_targets_and_fall_back_when_empty()
    {
        var withTargets = WorkspaceToolService.BuildSummary(
            PatchProposal(), [new ResolvedTarget(@"C:\w\src\a.ts", "src/a.ts")]);
        Assert.Contains("1 file(s)", withTargets, StringComparison.Ordinal);
        Assert.Contains("src/a.ts", withTargets, StringComparison.Ordinal);

        var withoutTargets = WorkspaceToolService.BuildSummary(PatchProposal(), []);
        Assert.Equal("Apply proposed change set.", withoutTargets);
    }

    [Fact]
    public void Command_summaries_truncate_long_arguments()
    {
        var longArgs = new[] { new string('x', 200) };
        var summary = WorkspaceToolService.BuildSummary(CommandProposal(longArgs), []);

        Assert.Contains("dotnet", summary, StringComparison.Ordinal);
        Assert.EndsWith("…' in the run worktree.", summary, StringComparison.Ordinal);

        var empty = WorkspaceToolService.BuildSummary(CommandProposal([]), []);
        Assert.StartsWith("Run 'dotnet ", empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Patches_whose_target_escapes_are_refused_before_any_write()
    {
        _workspace.WriteFile("keep.txt", "safe");
        var changeSet = ChangeSet.CreateNew("run_esc", Now);
        changeSet.AddFile("../outside.txt", FileChangeType.Modified, Now);

        var resolver = new RecordingPathResolver();
        var files = new RecordingFileAccess(_workspace.RootPath);
        var applied = PatchApplicationService.Apply(changeSet, _workspace.RootPath, resolver, files, new FakeClock(Now));

        Assert.False(applied.AllApplied);
        Assert.Contains("escapes the run boundary", applied.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabling_the_profile_after_creation_blocks_resume_planning()
    {
        var fixture = new SessionServiceFixture();
        var request = new CreateSessionRequest(
            fixture.WorkspaceId, "plan work", SessionMode.Plan,
            fixture.ModelProfileId, fixture.PolicyProfileId);
        var created = await fixture.Service.CreateAsync(request, $"create-{Guid.NewGuid():N}");
        var started = await fixture.Service.StartOrResumeAsync(
            created.Id, new StartRunRequest(null, null), $"run-{Guid.NewGuid():N}");

        // End the active run so the session is resumable, then remove eligibility.
        await fixture.Service.CancelAsync(created.Id, $"cancel-{Guid.NewGuid():N}");
        fixture.Profiles.Snapshot.Single().Disable(fixture.Clock.UtcNow);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.StartOrResumeAsync(
                started.Session.Id, new StartRunRequest(null, null), $"resume-{Guid.NewGuid():N}"));

        Assert.Equal("profile_not_plannable", conflict.Code);
    }

    [Fact]
    public void Idempotency_options_allow_operator_retention_tuning()
    {
        var options = new IdempotencyOptions { Retention = TimeSpan.FromMinutes(30) };

        Assert.Equal(TimeSpan.FromMinutes(30), options.Retention);
    }

    // ------------------------------------------------------------------ helpers

    private static ToolProposal CommandProposal(string[] args) => new(
        "ses", "run", "ws", ToolAction.RunCommand, CommandName: "dotnet", Arguments: args);

    private static ToolProposal PatchProposal() => new(
        "ses", "run", "ws", ToolAction.ApplyPatch, ChangeSetId: "chg_x");

    public void Dispose() => _workspace.Dispose();
}




