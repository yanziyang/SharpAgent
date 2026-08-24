namespace SharpAgent.Application.Idempotency;

/// <summary>Stable operation names recorded with each idempotency key.</summary>
public static class OperationNames
{
    public const string CreateSession = "create_session";
    public const string StartRun = "start_run";
    public const string CancelRun = "cancel_run";
    public const string ArchiveSession = "archive_session";
    public const string RestoreSession = "restore_session";
    public const string RegisterWorkspace = "register_workspace";
    public const string ResolveApproval = "resolve_approval";
}
