namespace SharpAgent.Domain.Sessions;

/// <summary>
/// Session and run lifecycle rules (technical design section 5.1).
/// <see cref="SessionStatus"/> is the session-level projection; <see cref="RunStatus"/> is the
/// run-level authority. Both share the same activity flow; the session adds the draft state.
/// </summary>
public static class SessionStateMachine
{
    public const string EntityName = "session";

    public static readonly IReadOnlyDictionary<SessionStatus, IReadOnlySet<SessionStatus>> Transitions =
        new Dictionary<SessionStatus, IReadOnlySet<SessionStatus>>
        {
            [SessionStatus.Draft] = Set(SessionStatus.Planning, SessionStatus.Executing),
            [SessionStatus.Planning] = ActiveNext(),
            [SessionStatus.Executing] = ActiveNext(),
            [SessionStatus.AwaitingApproval] = Set(
                SessionStatus.Executing,
                SessionStatus.Reviewing,
                SessionStatus.Interrupted,
                SessionStatus.Cancelled,
                SessionStatus.Failed),
            [SessionStatus.Reviewing] = Set(
                SessionStatus.Completed,
                SessionStatus.Failed,
                SessionStatus.Interrupted),
            [SessionStatus.Completed] = ResumeTargets(),
            [SessionStatus.Failed] = ResumeTargets(),
            [SessionStatus.Cancelled] = ResumeTargets(),
            [SessionStatus.Interrupted] = ResumeTargets(),
        };

    public static bool CanTransition(SessionStatus current, SessionStatus target) =>
        current != target && Transitions[current].Contains(target);

    public static void GuardTransition(SessionStatus current, SessionStatus target)
    {
        if (!CanTransition(current, target))
        {
            throw new InvalidStateTransitionException(
                EntityName, current.ToString(), target.ToString());
        }
    }

    public static bool IsActive(SessionStatus status) =>
        status is SessionStatus.Planning or SessionStatus.Executing
            or SessionStatus.AwaitingApproval or SessionStatus.Reviewing;

    public static bool IsTerminal(SessionStatus status) => !IsActive(status) && status != SessionStatus.Draft;

    private static HashSet<SessionStatus> ActiveNext() => new(
        [
            SessionStatus.AwaitingApproval,
            SessionStatus.Reviewing,
            SessionStatus.Completed,
            SessionStatus.Failed,
            SessionStatus.Interrupted,
            SessionStatus.Cancelled,
        ]);

    private static HashSet<SessionStatus> ResumeTargets() => new(
        [SessionStatus.Planning, SessionStatus.Executing]);

    private static HashSet<SessionStatus> Set(params SessionStatus[] statuses) => new(statuses);
}

