namespace SharpAgent.Domain.Sessions;

public enum SessionMode
{
    Plan = 0,
    Execute = 1,
}

public enum RunStatus
{
    Planning = 1,
    Executing = 2,
    AwaitingApproval = 3,
    Reviewing = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
    Interrupted = 8,
}
