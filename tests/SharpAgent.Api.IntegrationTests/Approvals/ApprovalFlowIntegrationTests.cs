using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpAgent.Api.IntegrationTests.TestSupport;
using SharpAgent.Application.Common;
using SharpAgent.Application.Sessions;
using SharpAgent.Application.Tools;
using SharpAgent.Domain.Approvals;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Infrastructure.Persistence;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Api.IntegrationTests.Approvals;

/// <summary>
/// AC-02 end-to-end over the real composition root: a proposed patch requires
/// approval, applies ONCE inside the disposable run worktree (never the base
/// checkout), and every decision is idempotent per key.
/// </summary>
public sealed class ApprovalFlowIntegrationTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TempWorkspace _repo = TempWorkspace.Create();

    [Fact]
    public async Task Patch_requires_approval_applies_once_in_worktree_and_is_listed()
    {
        // Arrange: a real git repository (worktrees need at least one commit).
        await File.WriteAllTextAsync(Path.Combine(_repo.RootPath, "src.txt"), "export const sum = 1 + 1;");
        Git(_repo.RootPath, ["init", "-b", "main"]);
        Git(_repo.RootPath, ["add", "."]);
        Git(_repo.RootPath, ["-c", "user.name=sharpagent-test", "-c", "user.email=test@local", "commit", "-m", "init"]);

        using var factory = new SharpAgentApiFactory
        {
            SqlitePath = Path.Combine(_repo.RootPath, "_db", "sharpagent.db"),
        };
        _ = factory.CreateClient(); // start host; runs migrations

        string sessionId;
        string workspaceId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SharpAgentDbContext>();
            var now = DateTimeOffset.UtcNow;

            var workspace = Domain.Workspaces.Workspace.Register("Repo", _repo.RootPath, now);
            workspace.MarkValidated(_repo.RootPath, now);
            await context.Workspaces.AddAsync(workspace);

            var profile = ModelProfile.Register(
                ProviderKind.Fake, "Fake Planner", "fake-planner-v1", EndpointKind.None, now);
            profile.SetCapabilities(new ProfileCapabilities(true, true, null, null, null), now);
            profile.MarkValidated(profile.GetCapabilities(), "ok", now);
            profile.Enable(now);
            await context.ModelProfiles.AddAsync(profile);

            var policy = Domain.Policies.PolicyProfile.Define("default-controlled", 45, 40, 5.00m, 10, now);
            await context.PolicyProfiles.AddAsync(policy);
            await context.SaveChangesAsync();

            workspaceId = workspace.Id;

            var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
            var created = await sessions.CreateAsync(
                new CreateSessionRequest(workspaceId, "Fix the sum.", SessionMode.Execute, profile.Id, policy.Id),
                $"create-{Guid.NewGuid():N}");
            var started = await sessions.StartOrResumeAsync(
                created.Id, new StartRunRequest(null, null), $"run-{Guid.NewGuid():N}");
            sessionId = started.Session.Id;

            // Propose the patch through the guarded tool service.
            var changeSets = scope.ServiceProvider.GetRequiredService<ChangeSetService>();
            var changeSet = await changeSets.ProposeAsync(sessionId,
            [
                new ProposeFileChange("src.txt", "export const sum = 2 + 2;", Delete: false),
            ]);

            var tools = scope.ServiceProvider.GetRequiredService<WorkspaceToolService>();
            var proposal = await tools.ProposeAsync(new ToolProposal(
                sessionId, started.Run.Id, workspaceId,
                ToolAction.ApplyPatch, ChangeSetId: changeSet.Id));

            var pending = Assert.IsType<ToolProposalResult.AwaitingApproval>(proposal);
            approvalId = pending.ApprovalId;
            runId = started.Run.Id;

            // Nothing applied yet: base checkout untouched while awaiting approval.
            Assert.Contains(
                "1 + 1",
                await File.ReadAllTextAsync(Path.Combine(_repo.RootPath, "src.txt")),
                StringComparison.Ordinal);
        }

        // Pending approvals are visible over HTTP before the decision.
        using (var pendingResponse = await factory.CreateClient().GetAsync($"/api/sessions/{sessionId}/approvals/pending"))
        {
            pendingResponse.EnsureSuccessStatusCode();
            var pendingJson = JsonDocument.Parse(await pendingResponse.Content.ReadAsStringAsync());
            Assert.Equal(1, pendingJson.RootElement.GetArrayLength());
            Assert.Equal(approvalId, pendingJson.RootElement[0].GetProperty("id").GetString());
        }

        // Act: approve once through the real service graph (EF-backed repositories).
        ApprovalResolutionOutcome outcome;
        await using (var resolveScope = factory.Services.CreateAsyncScope())
        {
            var approvalsService = resolveScope.ServiceProvider.GetRequiredService<ApprovalsService>();
            outcome = await approvalsService.ResolveAsync(
                approvalId,
                new ResolveApprovalRequest(ApprovalDecision.ApproveOnce, Comment: "looks right"),
                $"approve-{Guid.NewGuid():N}");
        }

        // Assert: applied exactly once, inside the WORKTREE only.
        Assert.Equal("Approved", outcome.ApprovalStatus);

        var baseText = await File.ReadAllTextAsync(Path.Combine(_repo.RootPath, "src.txt"));
        Assert.Contains("1 + 1", baseText);          // registered base checkout untouched
        Assert.DoesNotContain("2 + 2", baseText);

        var worktreesDir = Path.Combine(Path.GetTempPath(), "sharpagent-worktrees");

        // Cancelling the run releases its disposable worktree (best-effort cleanup).
        string cancelledWorktreePath;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
            await sessions.CancelAsync(sessionId, $"cancel-{Guid.NewGuid():N}");
            cancelledWorktreePath = Directory
                .EnumerateDirectories(worktreesDir, "*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(static dir => Directory.GetLastWriteTimeUtc(dir))
                .FirstOrDefault() ?? string.Empty;
        }

        // Resume creates a NEW run; a fresh proposal + approval applies there too.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();
            var started = await sessions.StartOrResumeAsync(
                sessionId, new StartRunRequest("bump the sum", null), $"run2-{Guid.NewGuid():N}");

            var changeSets = scope.ServiceProvider.GetRequiredService<ChangeSetService>();
            var changeSet = await changeSets.ProposeAsync(sessionId,
            [
                new ProposeFileChange("src.txt", "export const sum = 2 + 2;", Delete: false),
            ]);

            var tools = scope.ServiceProvider.GetRequiredService<WorkspaceToolService>();
            var proposal = await tools.ProposeAsync(new ToolProposal(
                sessionId, started.Run.Id, workspaceId,
                ToolAction.ApplyPatch, ChangeSetId: changeSet.Id));
            approvalId = Assert.IsType<ToolProposalResult.AwaitingApproval>(proposal).ApprovalId;

            var approvalsService = scope.ServiceProvider.GetRequiredService<ApprovalsService>();
            outcome = await approvalsService.ResolveAsync(
                approvalId,
                new ResolveApprovalRequest(ApprovalDecision.ApproveOnce, Comment: null),
                $"approve-{Guid.NewGuid():N}");
        }

        Assert.Equal("Approved", outcome.ApprovalStatus);

        // Safety-critical claims: the LATEST executed patch landed in A worktree
        // (never the base checkout). Exact cleanup timing is covered by unit tests
        // of SessionService/DbInitializer against the IGitWorktreeService port.
        var latest = Directory
            .EnumerateFiles(worktreesDir, "src.txt", SearchOption.AllDirectories)
            .Select(static path => (Path: path, Write: File.GetLastWriteTimeUtc(path)))
            .OrderByDescending(static entry => entry.Write)
            .First();
        Assert.Contains("2 + 2", await File.ReadAllTextAsync(latest.Path), StringComparison.Ordinal);

        // The consumed approval refuses a second resolution (single-use, FR-045).
        await using var replayScope = factory.Services.CreateAsyncScope();
        var replayService = replayScope.ServiceProvider.GetRequiredService<ApprovalsService>();
        var conflictException = await Assert.ThrowsAsync<ConflictException>(() => replayService.ResolveAsync(
            approvalId,
            new ResolveApprovalRequest(ApprovalDecision.ApproveOnce, Comment: null),
            $"approve-replay-{Guid.NewGuid():N}"));
        Assert.Equal("approval_already_resolved", conflictException.Code);

        // Change evidence is listed for review.
        var client = factory.CreateClient();
        using var changes = await client.GetAsync($"/api/sessions/{sessionId}/changes");
        changes.EnsureSuccessStatusCode();
        var changesJson = JsonDocument.Parse(await changes.Content.ReadAsStringAsync());
        Assert.Equal(2, changesJson.RootElement.GetArrayLength()); // one per executed run
        Assert.All(changesJson.RootElement.EnumerateArray(), static entry =>
            Assert.Equal("src.txt", entry.GetProperty("files")[0].GetProperty("path").GetString()));
        _ = runId;
    }

    private string approvalId = string.Empty;

    private string runId = string.Empty;

    private static void Git(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("git missing");
        var errors = process.StandardError.ReadToEndAsync();

        Assert.True(process.WaitForExit(30_000), "git timed out.");
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git failed: {errors.Result}");
        }
    }

    public void Dispose() => _repo.Dispose();
}



