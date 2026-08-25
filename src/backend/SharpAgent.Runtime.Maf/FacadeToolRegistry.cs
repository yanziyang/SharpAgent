using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Tools;
using SharpAgent.Domain.Sessions;

namespace SharpAgent.Runtime.Maf;

/// <summary>
/// Narrow facade tools exposed to the model (plan 11.1). Every call becomes a
/// canonical proposal through the bridge; no facade touches files, Git, shell,
/// provider configuration, or approval storage directly.
/// </summary>
public sealed class FacadeToolRegistry(
    RunContext context,
    IToolProposalBridge bridge,
    AgentToolOptions? options = null)
{
    public const string ReadToolName = "read";
    public const string WriteToolName = "write";
    public const string EditToolName = "edit";
    public const string BashToolName = "bash";
    public const string PowerShellToolName = "powershell";
    public const string GrepToolName = "grep";
    public const string FindToolName = "find";
    public const string LsToolName = "ls";
    public const string UpdateTodosToolName = "update_todos";
    public const string RepositoryStatusToolName = "repository_status";
    public const string ApplyPatchToolName = "apply_patch";
    public const string RunCommandToolName = "run_command";

    public IReadOnlyList<AIFunction> Create()
    {
        var configured = options ?? AgentToolOptions.Default;
        var tools = new List<AIFunction>();

        if (configured.IsEnabled(ReadToolName))
        {
            tools.Add(AIFunctionFactory.Create(Read, new AIFunctionFactoryOptions { Name = ReadToolName }));
        }

        if (configured.IsEnabled(LsToolName))
        {
            tools.Add(AIFunctionFactory.Create(ListDirectory, new AIFunctionFactoryOptions { Name = LsToolName }));
        }

        if (configured.IsEnabled(GrepToolName))
        {
            tools.Add(AIFunctionFactory.Create(SearchText, new AIFunctionFactoryOptions { Name = GrepToolName }));
        }

        if (configured.IsEnabled(FindToolName))
        {
            tools.Add(AIFunctionFactory.Create(FindFiles, new AIFunctionFactoryOptions { Name = FindToolName }));
        }

        if (configured.IsEnabled(UpdateTodosToolName))
        {
            tools.Add(AIFunctionFactory.Create(UpdateTodos, new AIFunctionFactoryOptions { Name = UpdateTodosToolName }));
        }

        if (configured.IsEnabled(RepositoryStatusToolName))
        {
            tools.Add(AIFunctionFactory.Create(RepositoryStatus, new AIFunctionFactoryOptions { Name = RepositoryStatusToolName }));
        }

        if (context.Mode == SessionMode.Execute)
        {
            // High-impact facades are approval-gated by the framework so the model
            // PROPOSES them; execution only happens after a canonical approval.
            if (configured.IsEnabled(WriteToolName))
            {
                tools.Add(new ApprovalRequiredAIFunction(
                    AIFunctionFactory.Create(Write, new AIFunctionFactoryOptions { Name = WriteToolName })));
            }

            if (configured.IsEnabled(EditToolName))
            {
                tools.Add(new ApprovalRequiredAIFunction(
                    AIFunctionFactory.Create(Edit, new AIFunctionFactoryOptions { Name = EditToolName })));
            }

            if (configured.IsEnabled(ApplyPatchToolName))
            {
                tools.Add(new ApprovalRequiredAIFunction(
                    AIFunctionFactory.Create(ApplyPatch, new AIFunctionFactoryOptions { Name = ApplyPatchToolName })));
            }

            if (configured.IsEnabled(BashToolName))
            {
                tools.Add(new ApprovalRequiredAIFunction(
                    AIFunctionFactory.Create(Bash, new AIFunctionFactoryOptions { Name = BashToolName })));
            }

            if (configured.IsEnabled(PowerShellToolName))
            {
                tools.Add(new ApprovalRequiredAIFunction(
                    AIFunctionFactory.Create(PowerShell, new AIFunctionFactoryOptions { Name = PowerShellToolName })));
            }

            if (configured.IsEnabled(RunCommandToolName))
            {
                tools.Add(new ApprovalRequiredAIFunction(
                    AIFunctionFactory.Create(RunCommand, new AIFunctionFactoryOptions { Name = RunCommandToolName })));
            }
        }

        return tools;
    }

    [Description("Read a text file inside the workspace. Returns bounded, redacted content.")]
    private Task<string> Read(
        [Description("Workspace-relative file path.")] string path,
        CancellationToken cancellationToken) =>
        ProposeAsync(new ToolProposal(context.SessionId, context.RunId, context.WorkspaceId, ToolAction.ReadFile, RelativePath: path), cancellationToken);

    [Description("List the top-level entries of a workspace-relative directory.")]
    private Task<string> ListDirectory(
        [Description("Workspace-relative directory path, or '.' for the root.")] string path,
        CancellationToken cancellationToken) =>
        ProposeAsync(new ToolProposal(context.SessionId, context.RunId, context.WorkspaceId, ToolAction.ListDirectory, RelativePath: path), cancellationToken);

    [Description("Search workspace text files recursively for a query. Results are bounded and redacted.")]
    private Task<string> SearchText(
        [Description("Workspace-relative directory path.")] string path,
        [Description("Text to find.")] string query,
        CancellationToken cancellationToken) =>
        ProposeAsync(new ToolProposal(context.SessionId, context.RunId, context.WorkspaceId, ToolAction.SearchText, RelativePath: path, SearchQuery: query, Recursive: true), cancellationToken);

    [Description("Find files recursively by a workspace-relative name pattern such as *.cs. Results are bounded.")]
    private Task<string> FindFiles(
        [Description("Workspace-relative directory path.")] string path,
        [Description("File name pattern, for example *.cs.")] string namePattern,
        CancellationToken cancellationToken) =>
        ProposeAsync(new ToolProposal(context.SessionId, context.RunId, context.WorkspaceId, ToolAction.FindFiles, RelativePath: path, NamePattern: namePattern), cancellationToken);

    [Description("Show the repository working-tree status.")]
    private Task<string> RepositoryStatus(CancellationToken cancellationToken) =>
        ProposeAsync(new ToolProposal(context.SessionId, context.RunId, context.WorkspaceId, ToolAction.RepositoryStatus), cancellationToken);

    [Description("Replace the visible todo plan. Payload is a JSON array of {text, done} objects. Returns 'ok'.")]
    private static Task<string> UpdateTodos(
        [Description("JSON array, e.g. [{\"text\":\"Step one\",\"done\":false}]")] string todosJson,
        CancellationToken cancellationToken) =>
        Task.FromResult(ValidateTodosJson(todosJson) ? "ok" : "error: todos must be a JSON array of {text, done} objects.");

    [Description("Apply a previously proposed change set inside the run worktree. Requires developer approval.")]
    private Task<string> ApplyPatch(
        [Description("The change set id that was proposed.")] string changeSetId,
        CancellationToken cancellationToken) =>
        ProposeAsync(new ToolProposal(context.SessionId, context.RunId, context.WorkspaceId, ToolAction.ApplyPatch, ChangeSetId: changeSetId), cancellationToken);

    [Description("Create or replace a text file inside the run worktree. Requires developer approval.")]
    private Task<string> Write(
        [Description("Workspace-relative file path.")] string path,
        [Description("Complete UTF-8 text content, bounded to the configured file-change limit.")] string content,
        CancellationToken cancellationToken) =>
        ProposeAsync(new ToolProposal(context.SessionId, context.RunId, context.WorkspaceId, ToolAction.WriteFile, RelativePath: path, Content: content), cancellationToken);

    [Description("Replace one unique text span in a workspace file. Requires developer approval.")]
    private Task<string> Edit(
        [Description("Workspace-relative file path.")] string path,
        [Description("Existing text that must match exactly once.")] string oldText,
        [Description("Replacement text.")] string newText,
        CancellationToken cancellationToken) =>
        ProposeAsync(new ToolProposal(context.SessionId, context.RunId, context.WorkspaceId, ToolAction.EditFile, RelativePath: path, OldText: oldText, NewText: newText), cancellationToken);

    [Description("Run one approved Bash diagnostic from the server-side focused command catalog. Arbitrary shell scripts are rejected.")]
    private Task<string> Bash(
        [Description("Exact catalog command, for example pwd or bash --version.")] string command,
        CancellationToken cancellationToken) =>
        ProposeCommandAsync("bash", command, arguments: null, cancellationToken);

    [Description("Run one approved Windows PowerShell diagnostic from the focused server-side catalog. Arbitrary scripts are rejected.")]
    private Task<string> PowerShell(
        [Description("Exact catalog command, for example Get-Date -Format o or Get-Location.")] string command,
        CancellationToken cancellationToken) =>
        ProposeCommandAsync("powershell", command, arguments: null, cancellationToken);

    [Description("Run a focused command from the approved catalog in the run worktree. Requires developer approval.")]
    private Task<string> RunCommand(
        [Description("Command name from the approved catalog (for example dotnet, npm, node).")] string commandName,
        [Description("Optional command arguments.")] IReadOnlyList<string>? arguments,
        CancellationToken cancellationToken) =>
        ProposeAsync(new ToolProposal(context.SessionId, context.RunId, context.WorkspaceId, ToolAction.RunCommand, CommandName: commandName, Arguments: arguments), cancellationToken);

    private Task<string> ProposeCommandAsync(
        string commandName,
        string command,
        IReadOnlyList<string>? arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var commandArguments = new List<string> { command };
        if (arguments is not null)
        {
            commandArguments.AddRange(arguments);
        }

        return ProposeAsync(
            new ToolProposal(
                context.SessionId,
                context.RunId,
                context.WorkspaceId,
                ToolAction.RunCommand,
                CommandName: commandName,
                Arguments: commandArguments),
            cancellationToken);
    }

    private async Task<string> ProposeAsync(ToolProposal proposal, CancellationToken cancellationToken)
    {
        var outcome = await bridge.ProposeAsync(proposal, cancellationToken).ConfigureAwait(false);

        return outcome.Status switch
        {
            ToolProposalStatus.Executed => outcome.OutputPreview ?? "Done.",
            ToolProposalStatus.AwaitingApproval => $"Action proposed and awaiting approval: {outcome.ApprovalId}",
            ToolProposalStatus.Denied => $"Action not permitted: {outcome.SafeMessage}",
            _ => $"Action failed: {outcome.SafeMessage ?? "unknown error"}",
        };
    }

    private static bool ValidateTodosJson(string todosJson)
    {
        try
        {
            using var document = JsonDocument.Parse(todosJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("text", out var text)
                    || text.ValueKind != JsonValueKind.String
                    || !item.TryGetProperty("done", out var done)
                    || done.ValueKind != JsonValueKind.True && done.ValueKind != JsonValueKind.False)
                {
                    return false;
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
