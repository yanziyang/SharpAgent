using SharpAgent.Domain.Common;
using SharpAgent.Domain.Sessions;
using Xunit;

namespace SharpAgent.Domain.Tests.Sessions;

public sealed class SessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    private static Session NewSession(SessionMode mode = SessionMode.Plan) =>
        Session.CreateNew("ws_test", "Investigate the failing pricing test", mode, "model_x", "pol_default", Now);

    [Fact]
    public void CreateNew_requires_all_references()
    {
        Assert.Throws<ArgumentException>(() =>
            Session.CreateNew(string.Empty, "task", SessionMode.Plan, "m", "p", Now));
        Assert.Throws<ArgumentException>(() =>
            Session.CreateNew("ws", string.IsNullOrWhiteSpace(" ") ? " " : " ", SessionMode.Plan, "m", "p", Now));
        Assert.Throws<ArgumentException>(() =>
            Session.CreateNew("ws", "task", SessionMode.Plan, string.Empty, "p", Now));
        Assert.Throws<ArgumentException>(() =>
            Session.CreateNew("ws", "task", SessionMode.Plan, "m", string.Empty, Now));
    }

    [Fact]
    public void BeginRun_from_draft_plan_mode_enters_planning_with_run_one()
    {
        var session = NewSession();

        var run = session.BeginRun(Now.AddMinutes(1));

        Assert.Equal(SessionStatus.Planning, session.Status);
        Assert.Equal(run.Id, session.ActiveRunId);
        Assert.Equal(1, run.Sequence);
        Assert.Equal(RunStatus.Planning, run.Status);
        Assert.Equal(session.Id, run.SessionId);
    }

    [Fact]
    public void BeginRun_execute_mode_enters_executing()
    {
        var session = NewSession(SessionMode.Execute);
        session.BeginRun(Now.AddMinutes(1));

        Assert.Equal(SessionStatus.Executing, session.Status);
        Assert.Equal(RunStatus.Executing, session.Runs[0].Status);
    }

    [Fact]
    public void BeginRun_while_a_run_is_active_is_rejected()
    {
        var session = NewSession();
        session.BeginRun(Now.AddMinutes(1));

        Assert.Throws<InvalidStateTransitionException>(() => session.BeginRun(Now.AddMinutes(2)));
    }

    [Theory]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Failed)]
    [InlineData(SessionStatus.Cancelled)]
    [InlineData(SessionStatus.Interrupted)]
    public void Resume_from_terminal_states_creates_a_new_run_and_keeps_history(SessionStatus terminal)
    {
        var session = NewSession(SessionMode.Execute);
        var first = session.BeginRun(Now.AddMinutes(1));

        // Drive the first run to the requested terminal state.
        switch (terminal)
        {
            case SessionStatus.Completed:
                first.Complete("done", Now.AddMinutes(2));
                break;
            case SessionStatus.Failed:
                first.Fail("provider error", Now.AddMinutes(2));
                break;
            case SessionStatus.Cancelled:
                first.Cancel("user stop", Now.AddMinutes(2));
                break;
            default:
                first.Interrupt("restart", Now.AddMinutes(2));
                break;
        }

        session.ApplyTransition(terminal, Now.AddMinutes(2));
        Assert.Null(session.ActiveRunId);

        var resumed = session.BeginRun(Now.AddMinutes(3), instruction: "continue", resumeSourceRunId: first.Id);

        Assert.NotEqual(first.Id, resumed.Id);
        Assert.Equal(2, resumed.Sequence);
        Assert.Equal(first.Id, resumed.ResumeSourceRunId);
        Assert.Equal(first.Id, session.Runs[0].Id); // history retained
        Assert.Equal(resumed.Id, session.ActiveRunId);
        Assert.Equal("continue", session.LastInstruction);
    }

    [Fact]
    public void Resume_rejects_foreign_resume_source_run()
    {
        var session = NewSession();

        Assert.Throws<ArgumentException>(
            () => session.BeginRun(Now.AddMinutes(1), resumeSourceRunId: "run_does_not_exist"));
    }

    [Fact]
    public void ApplyTransition_updates_status_and_clears_active_run_on_terminal()
    {
        var session = NewSession();
        session.BeginRun(Now.AddMinutes(1));

        session.ApplyTransition(SessionStatus.Reviewing, Now.AddMinutes(2));
        session.ApplyTransition(SessionStatus.Completed, Now.AddMinutes(3));

        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Null(session.ActiveRunId);
        Assert.Equal(Now.AddMinutes(3), session.UpdatedAtUtc);
    }

    [Fact]
    public void ApplyTransition_rejects_illegal_moves()
    {
        var session = NewSession();

        Assert.Throws<InvalidStateTransitionException>(
            () => session.ApplyTransition(SessionStatus.AwaitingApproval, Now.AddMinutes(1)));
    }

    [Fact]
    public void CancelActiveRun_marks_run_cancelled_and_session_cancelled()
    {
        var session = NewSession(SessionMode.Execute);
        var run = session.BeginRun(Now.AddMinutes(1));

        session.CancelActiveRun("cancelled by developer", Now.AddMinutes(2));

        Assert.Equal(RunStatus.Cancelled, run.Status);
        Assert.Equal("cancelled by developer", run.StopReason);
        Assert.NotNull(run.EndedAtUtc);
        Assert.Equal(SessionStatus.Cancelled, session.Status);
    }

    [Fact]
    public void Cancel_without_active_run_is_rejected()
    {
        var session = NewSession();

        Assert.Throws<InvalidStateTransitionException>(
            () => session.CancelActiveRun("nope", Now.AddMinutes(1)));
    }

    [Fact]
    public void Fail_and_interrupt_require_an_active_run_too()
    {
        var session = NewSession();

        Assert.Throws<InvalidStateTransitionException>(() => session.FailActiveRun("x", Now));
        Assert.Throws<InvalidStateTransitionException>(() => session.InterruptActiveRun("x", Now));
    }

    [Fact]
    public void Archive_hides_terminal_sessions_and_is_idempotent()
    {
        var session = NewSession();
        var run = session.BeginRun(Now.AddMinutes(1));
        run.Complete("ok", Now.AddMinutes(2));
        session.ApplyTransition(SessionStatus.Completed, Now.AddMinutes(2));

        session.Archive(Now.AddMinutes(3));
        session.Archive(Now.AddMinutes(4)); // idempotent

        Assert.Equal(Now.AddMinutes(3), session.ArchivedAtUtc);

        session.Restore(Now.AddMinutes(5));
        session.Restore(Now.AddMinutes(6)); // idempotent

        Assert.Null(session.ArchivedAtUtc);
    }

    [Fact]
    public void Archive_is_blocked_for_draft_sessions_with_active_runs_only()
    {
        var active = NewSession();
        active.BeginRun(Now.AddMinutes(1));

        Assert.Throws<InvalidStateTransitionException>(() => active.Archive(Now.AddMinutes(2)));

        // A plain draft has no active run and may be archived.
        var draft = NewSession();
        draft.Archive(Now.AddMinutes(1));
        Assert.NotNull(draft.ArchivedAtUtc);
    }
}
