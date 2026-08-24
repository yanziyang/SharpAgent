namespace SharpAgent.Domain.Sessions;

/// <summary>Run-level authoritative transitions (technical design section 5.1).</summary>
public static class RunStateMachine
{
    public const string EntityName = "run";

    public static readonly IReadOnlyDictionary<RunStatus, IReadOnlySet<RunStatus>> Transitions =
        new Dictionary<RunStatus, IReadOnlySet<RunStatus>>
        {
            [RunStatus.Planning] = Set(
                RunStatus.AwaitingApproval,
                RunStatus.Reviewing,
                RunStatus.Completed,
                RunStatus.Failed,
                RunStatus.Interrupted,
                RunStatus.Cancelled),
            [RunStatus.Executing] = Set(
                RunStatus.AwaitingApproval,
                RunStatus.Reviewing,
                RunStatus.Completed,
                RunStatus.Failed,
                RunStatus.Interrupted,
                RunStatus.Cancelled),
            [RunStatus.AwaitingApproval] = Set(
                RunStatus.Executing,
                RunStatus.Reviewing,
                RunStatus.Interrupted,
                RunStatus.Cancelled,
                RunStatus.Failed),
            [RunStatus.Reviewing] = Set(
                RunStatus.Completed,
                RunStatus.Failed,
                RunStatus.Interrupted),
            [RunStatus.Completed] = Set(),
            [RunStatus.Failed] = Set(),
            [RunStatus.Cancelled] = Set(),
            [RunStatus.Interrupted] = Set(),
        };

    public static bool CanTransition(RunStatus current, RunStatus target) =>
        current != target && Transitions[current].Contains(target);

    public static void GuardTransition(RunStatus current, RunStatus target)
    {
        if (!CanTransition(current, target))
        {
            throw new InvalidStateTransitionException(
                EntityName, current.ToString(), target.ToString());
        }
    }

    /// <summary>A run is active while it can still make progress without a resume.</summary>
    public static bool IsActive(RunStatus status) =>
        status is RunStatus.Planning or RunStatus.Executing
            or RunStatus.AwaitingApproval or RunStatus.Reviewing;

    public static bool IsTerminal(RunStatus status) => !IsActive(status);

    private static HashSet<RunStatus> Set(params RunStatus[] statuses) => new(statuses);
}
