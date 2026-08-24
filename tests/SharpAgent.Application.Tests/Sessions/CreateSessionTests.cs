using Xunit;
using SharpAgent.Application.Tests.Support;
using SharpAgent.Application.Common;
using SharpAgent.Application.Idempotency;
using SharpAgent.Application.Sessions;
using SharpAgent.Domain.Auditing;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Todos;
using SharpAgent.TestKit.Fakes;

namespace SharpAgent.Application.Tests.Sessions;

public sealed class CreateSessionTests
{
    private static CreateSessionRequest ValidRequest(SessionServiceFixture fixture, SessionMode mode = SessionMode.Plan) => new(
        WorkspaceId: fixture.WorkspaceId,
        Task: "Investigate the failing pricing test and propose a plan.",
        Mode: mode,
        ModelProfileId: fixture.ModelProfileId,
        PolicyProfileId: fixture.PolicyProfileId);

    [Fact]
    public async Task Creating_a_session_persists_a_draft_and_appends_the_first_event()
    {
        var fixture = new SessionServiceFixture();

        var dto = await fixture.Service.CreateAsync(ValidRequest(fixture), idempotencyKey: "key-1");

        Assert.Equal(SessionStatus.Draft, dto.Status);
        Assert.Equal(SessionMode.Plan, dto.Mode);
        Assert.Empty(dto.Runs);
        Assert.False(dto.Archived);

        var stored = Assert.Single(fixture.Sessions.Snapshot);
        Assert.Equal(dto.Id, stored.Id);

        var auditEvent = Assert.Single(await fixture.Events.ReplayAsync(stored.Id, CancellationToken.None));
        Assert.Equal(1, auditEvent.Sequence); // monotonic per-session sequence starts at 1
        Assert.Equal(AuditEventTypes.SessionCreated, auditEvent.Type);
        Assert.Equal(1, fixture.UnitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Unknown_workspace_is_rejected_with_field_error()
    {
        var fixture = new SessionServiceFixture();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.CreateAsync(ValidRequest(fixture) with { WorkspaceId = "ws_nope" }, "k"));

        Assert.True(exception.Errors.ContainsKey("workspaceId"));
        Assert.Empty(fixture.Sessions.Snapshot);
    }

    [Fact]
    public async Task Unknown_model_profile_or_policy_is_rejected()
    {
        var fixture = new SessionServiceFixture();

        var profileError = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.CreateAsync(ValidRequest(fixture) with { ModelProfileId = "model_nope" }, "k"));
        Assert.True(profileError.Errors.ContainsKey("modelProfileId"));

        var policyError = await Assert.ThrowsAsync<ValidationException>(() =>
            fixture.Service.CreateAsync(ValidRequest(fixture) with { PolicyProfileId = "pol_nope" }, "k"));
        Assert.True(policyError.Errors.ContainsKey("policyProfileId"));
    }

    [Fact]
    public async Task Blank_task_text_is_rejected()
    {
        var fixture = new SessionServiceFixture();

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => fixture.Service.CreateAsync(ValidRequest(fixture) with { Task = "   " }, "k"));

        Assert.True(exception.Errors.ContainsKey("task"));
    }

    [Fact]
    public async Task Oversized_task_text_is_rejected()
    {
        var fixture = new SessionServiceFixture();
        var request = ValidRequest(fixture) with { Task = new string('x', SessionService.MaxTaskLength + 1) };

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => fixture.Service.CreateAsync(request, "k"));

        Assert.True(exception.Errors.ContainsKey("task"));
    }

    [Fact]
    public async Task Execute_mode_requires_a_validated_streaming_tool_profile()
    {
        var unvalidatedFixture = new SessionServiceFixture(validatedStreamingToolProfile: false);

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            unvalidatedFixture.Service.CreateAsync(ValidRequest(unvalidatedFixture, SessionMode.Execute), "k-exec"));

        Assert.True(exception.Errors.TryGetValue("modelProfileId", out var messages));
        Assert.Contains("Execute mode", string.Join(" ", messages!), StringComparison.Ordinal);

        // Plan-only remains available on that same unvalidated profile (E2E-08 seed).
        var planSession = await unvalidatedFixture.Service.CreateAsync(
            ValidRequest(unvalidatedFixture, SessionMode.Plan), "k-plan");
        Assert.Equal(SessionMode.Plan, planSession.Mode);

        var validatedFixture = new SessionServiceFixture();
        var executeSession = await validatedFixture.Service.CreateAsync(
            ValidRequest(validatedFixture, SessionMode.Execute), "k-exec-ok");
        Assert.Equal(SessionMode.Execute, executeSession.Mode);
    }

    [Fact]
    public async Task Same_idempotency_key_replays_without_creating_again()
    {
        var fixture = new SessionServiceFixture();

        var first = await fixture.Service.CreateAsync(ValidRequest(fixture), "same-key");
        var second = await fixture.Service.CreateAsync(ValidRequest(fixture), "same-key");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(fixture.Sessions.Snapshot);
        Assert.Single(await fixture.Events.ReplayAsync(first.Id, CancellationToken.None));
        Assert.Equal(1, fixture.Idempotency.SaveCalls);
    }

    [Fact]
    public async Task Same_key_with_different_payload_is_a_conflict()
    {
        var fixture = new SessionServiceFixture();

        await fixture.Service.CreateAsync(ValidRequest(fixture), "dup-key");

        var conflict = await Assert.ThrowsAsync<ConflictException>(
            () => fixture.Service.CreateAsync(
                ValidRequest(fixture) with { Task = "Different task" }, "dup-key"));

        Assert.Equal("idempotency_conflict", conflict.Code);
        Assert.Single(fixture.Sessions.Snapshot);
    }

    [Fact]
    public async Task Expired_keys_allow_fresh_executions_after_retention()
    {
        var fixture = new SessionServiceFixture();

        var first = await fixture.Service.CreateAsync(ValidRequest(fixture), "expiring-key");
        fixture.Clock.Advance(IdempotencyService.DefaultRetention + TimeSpan.FromMinutes(1));

        var second = await fixture.Service.CreateAsync(ValidRequest(fixture), "expiring-key");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, fixture.Sessions.Snapshot.Count);
    }
}


