using System.Diagnostics;
using SharpAgent.Domain.Common;
using SharpAgent.Domain.Sessions;
using Xunit;

namespace SharpAgent.Domain.Tests.Sessions;

/// <summary>Exhaustive run-level transition proof (run status is the authority).</summary>
public sealed class RunStateTransitionTests
{
    public static TheoryData<RunStatus> AllStatuses() =>
        new([.. Enum.GetValues<RunStatus>()]);

    private static HashSet<RunStatus> ExpectedTargets(RunStatus current) => current switch
    {
        RunStatus.Planning or RunStatus.Executing => new HashSet<RunStatus>(
        [
            RunStatus.AwaitingApproval, RunStatus.Reviewing, RunStatus.Completed,
            RunStatus.Failed, RunStatus.Interrupted, RunStatus.Cancelled,
        ]),
        RunStatus.AwaitingApproval => new HashSet<RunStatus>(
        [
            RunStatus.Executing, RunStatus.Reviewing, RunStatus.Interrupted,
            RunStatus.Cancelled, RunStatus.Failed,
        ]),
        RunStatus.Reviewing => new HashSet<RunStatus>(
        [
            RunStatus.Completed, RunStatus.Failed, RunStatus.Interrupted,
        ]),
        // Terminal runs never transition; resume creates a NEW run.
        RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled or RunStatus.Interrupted => [],
        _ => throw new UnreachableException(),
    };

    [Theory]
    [MemberData(nameof(AllStatuses))]
    public void Transition_map_matches_the_design_table_exactly(RunStatus current)
    {
        var expected = ExpectedTargets(current);

        foreach (var target in Enum.GetValues<RunStatus>())
        {
            var can = RunStateMachine.CanTransition(current, target);

            if (current == target)
            {
                Assert.False(can);
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
    public void Terminal_runs_cannot_transition_anywhere()
    {
        foreach (var terminal in Enum.GetValues<RunStatus>().Where(RunStateMachine.IsTerminal))
        {
            foreach (var target in Enum.GetValues<RunStatus>())
            {
                Assert.False(
                    RunStateMachine.CanTransition(terminal, target),
                    $"Terminal {terminal} must not move to {target}.");
            }
        }
    }
}
