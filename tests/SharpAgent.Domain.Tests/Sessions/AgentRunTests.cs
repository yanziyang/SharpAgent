using SharpAgent.Domain.Common;
using SharpAgent.Domain.Sessions;
using Xunit;

namespace SharpAgent.Domain.Tests.Sessions;

public sealed class AgentRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    private static (Session Session, AgentRun Run) NewActiveRun(SessionMode mode = SessionMode.Execute)
    {
        var session = Session.CreateNew("ws", "task", mode, "m", "p", Now);
        var run = session.BeginRun(Now.AddMinutes(1));
        return (session, run);
    }

    [Fact]
    public void Complete_sets_summary_and_terminal_state()
    {
        var (_, run) = NewActiveRun();

        run.Complete("all done", Now.AddMinutes(2));

        Assert.Equal("all done", run.FinalSummary);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.NotNull(run.EndedAtUtc);
        Assert.Null(run.StopReason);
    }

    [Fact]
    public void Fail_cancel_and_interrupt_record_reason_and_end_time()
    {
        var failRun = NewActiveRun().Run;
        failRun.Fail("provider unreachable", Now.AddMinutes(2));
        Assert.Equal(RunStatus.Failed, failRun.Status);
        Assert.Equal("provider unreachable", failRun.StopReason);

        var cancelRun = NewActiveRun().Run;
        cancelRun.Cancel("user requested", Now.AddMinutes(2));
        Assert.Equal(RunStatus.Cancelled, cancelRun.Status);
        Assert.NotNull(cancelRun.CancelRequestedAtUtc);

        var interruptRun = NewActiveRun().Run;
        interruptRun.Interrupt("host restart", Now.AddMinutes(2));
        Assert.Equal(RunStatus.Interrupted, interruptRun.Status);
    }

    [Fact]
    public void Approval_wait_then_continue_round_trip_is_legal()
    {
        var (_, run) = NewActiveRun();

        run.TransitionTo(RunStatus.AwaitingApproval, Now.AddMinutes(2));
        run.TransitionTo(RunStatus.Executing, Now.AddMinutes(3));

        Assert.Equal(RunStatus.Executing, run.Status);
        Assert.Null(run.EndedAtUtc); // still active
    }

    [Fact]
    public void Terminal_runs_are_immutable()
    {
        var (_, run) = NewActiveRun();
        run.Fail("boom", Now.AddMinutes(2));

        Assert.Throws<InvalidStateTransitionException>(() => run.Complete("late", Now.AddMinutes(3)));
        Assert.Throws<InvalidStateTransitionException>(() => run.Cancel("late", Now.AddMinutes(3)));
        Assert.Throws<InvalidStateTransitionException>(() => run.Fail("again", Now.AddMinutes(3)));
        Assert.Throws<InvalidStateTransitionException>(
            () => run.RecordCancellationRequest(Now.AddMinutes(3)));
    }

    [Fact]
    public void Self_transitions_are_rejected()
    {
        var (_, run) = NewActiveRun();

        Assert.Throws<InvalidStateTransitionException>(
            () => run.TransitionTo(RunStatus.Executing, Now.AddMinutes(2)));
    }
}
