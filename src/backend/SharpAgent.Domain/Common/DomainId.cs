namespace SharpAgent.Domain.Common;

/// <summary>Opaque identifier generator. IDs use a short prefix plus a GUID "N" form.</summary>
public static class DomainId
{
    public static string NewWorkspaceId() => Prefixed("ws");
    public static string NewSessionId() => Prefixed("ses");
    public static string NewRunId() => Prefixed("run");
    public static string NewTodoId() => Prefixed("todo");
    public static string NewApprovalId() => Prefixed("apr");
    public static string NewToolExecutionId() => Prefixed("tex");
    public static string NewChangeSetId() => Prefixed("chg");
    public static string NewFileChangeId() => Prefixed("flc");
    public static string NewModelProfileId() => Prefixed("model");
    public static string NewPolicyProfileId() => Prefixed("pol");
    public static string NewLeaseId() => Prefixed("lse");

    public static string NewCorrelationId() => Prefixed("corr");
    public static string NewUsageId() => Prefixed("use");

    /// <summary>Audit event identifiers embed the session sequence for readability.</summary>
    public static string NewEventId(long sequence) => $"evt_{sequence:D10}_{Guid.NewGuid():N}";

    private static string Prefixed(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
