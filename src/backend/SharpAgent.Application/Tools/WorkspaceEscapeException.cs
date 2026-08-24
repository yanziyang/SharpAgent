namespace SharpAgent.Application.Tools;

/// <summary>A proposed target left the registered workspace boundary (AC-07).</summary>
public sealed class WorkspaceEscapeException(string safeMessage) : Exception(safeMessage)
{
    public const string Code = "workspace_escape";
}
