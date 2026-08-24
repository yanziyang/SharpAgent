using System.Diagnostics;
using SharpAgent.Domain.Common;
using SharpAgent.Domain.Sessions;
using Xunit;

namespace SharpAgent.Domain.Tests.Sessions;

/// <summary>
/// Exhaustively proves the session transition map equals the design table
/// (technical design section 5.1) and that guards reject every other pair.
/// </summary>
public sealed class SessionStateTransitionTests
{
    public static TheoryData<SessionStatus> AllStatuses() =>
        new([.. Enum.GetValues<SessionStatus>()]);

    private static HashSet<SessionStatus> ExpectedTargets(SessionStatus current) => current switch
    {
        SessionStatus.Draft => Set(SessionStatus.Planning, SessionStatus.Executing),
        SessionStatus.Planning or SessionStatus.Executing => Set(
            SessionStatus.AwaitingApproval, SessionStatus.Reviewing, SessionStatus.Completed,
            SessionStatus.Failed, SessionStatus.Interrupted, SessionStatus.Cancelled),
        SessionStatus.AwaitingApproval => Set(
            SessionStatus.Executing, SessionStatus.Reviewing, SessionStatus.Interrupted,
            SessionStatus.Cancelled, SessionStatus.Failed),
        SessionStatus.Reviewing => Set(SessionStatus.Completed, SessionStatus.Failed, SessionStatus.Interrupted),
        // Terminal states resume into a new run.
        SessionStatus.Completed or SessionStatus.Failed
            or SessionStatus.Cancelled or SessionStatus.Interrupted => Set(
            SessionStatus.Planning, SessionStatus.Executing),
        _ => throw new UnreachableException(),
    };

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void Transition_map_matches_the_design_table_exactly(SessionStatus current)
    {
        var expected = ExpectedTargets(current);

        foreach (var target in Enum.GetValues<SessionStatus>())
        {
            var can = SessionStateMachine.CanTransition(current, target);

            if (current == target)
            {
                Assert.False(can, $"Self-transition {current} must be rejected.");
            }
            else if (expected.Contains(target))
            {
                Assert.True(can, $"{current} -> {target} should be allowed.");
            }
            else
            {
                Assert.False(can, $"{current} -> {target} should be rejected.");
            }
        }
    }

    [Fact]
    public void Guard_throws_for_rejected_transitions()
    {
        var exception = Record.Exception(() =>
            SessionStateMachine.GuardTransition(SessionStatus.Completed, SessionStatus.AwaitingApproval));

        var invalid = Assert.IsType<InvalidStateTransitionException>(exception);
        Assert.Equal("session", invalid.Entity);
        Assert.Equal("Completed", invalid.Current);
        Assert.Equal("AwaitingApproval", invalid.Target);
    }

    [Fact]
    public void Guard_allows_resume_from_every_terminal_state()
    {
        foreach (var terminal in Enum.GetValues<SessionStatus>().Where(SessionStateMachine.IsTerminal))
        {
            foreach (var resumeTarget in new[] { SessionStatus.Planning, SessionStatus.Executing })
            {
                var exception = Record.Exception(
                    () => SessionStateMachine.GuardTransition(terminal, resumeTarget));

                Assert.Null(exception);
            }
        }
    }

    [Theory]
    [InlineData(SessionStatus.Draft, false, false)]
    [InlineData(SessionStatus.Planning, true, false)]
    [InlineData(SessionStatus.Executing, true, false)]
    [InlineData(SessionStatus.AwaitingApproval, true, false)]
    [InlineData(SessionStatus.Reviewing, true, false)]
    [InlineData(SessionStatus.Completed, false, true)]
    [InlineData(SessionStatus.Failed, false, true)]
    [InlineData(SessionStatus.Cancelled, false, true)]
    [InlineData(SessionStatus.Interrupted, false, true)]
    public void Activity_classification_is_correct(
        SessionStatus status,
        bool expectedActive,
        bool expectedTerminal)
    {
        Assert.Equal(expectedActive, SessionStateMachine.IsActive(status));
        Assert.Equal(expectedTerminal, SessionStateMachine.IsTerminal(status));
    }

    private static HashSet<SessionStatus> Set(params SessionStatus[] statuses) => new(statuses);
}
