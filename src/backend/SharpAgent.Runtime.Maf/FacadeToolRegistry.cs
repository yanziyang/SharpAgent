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
    IToolProposalBridge bridge)
{
    public const string UpdateTodosToolName = "update_todos";
    public const string ApplyPatchToolName = "apply_patch";
    public const string RunCommandToolName = "run_command";

    public IReadOnlyList<AIFunction> Create()
    {
        var tools = new List<AIFunction>
        {
            AIFunctionFactory.Create(ReadFile, new AIFunctionFactoryOptions { Name = "read_file" }),
            AIFunctionFactory.Create(ListDirectory, new AIFunctionFactoryOptions { Name = "list_directory" }),
            AIFunctionFactory.Create(SearchText, new AIFunctionFactoryOptions { Name = "search_text" }),
            AIFunctionFactory.Create(RepositoryStatus, new AIFunctionFactoryOptions { Name = "repository_status" }),
            AIFunctionFactory.Create(UpdateTodos, new AIFunctionFactoryOptions { Name = UpdateTodosToolName }),
        };

        if (context.Mode == SessionMode.Execute)
        {
            // High-impact facades are approval-gated by the framework so the model
            // PROPOSES them; execution only happens after a canonical approval.
            tools.Add(new ApprovalRequiredAIFunction(
                AIFunctionFactory.Create(ApplyPatch, new AIFunctionFactoryOptions { Name = ApplyPatchToolName })));
            tools.Add(new ApprovalRequiredAIFunction(
                AIFunctionFactory.Create(RunCommand, new AIFunctionFactoryOptions { Name = RunCommandToolName })));
        }

        return tools;
    }

    [Description("Read a text file inside the workspace. Returns bounded, redacted content.")]
    private Task<string> ReadFile(
        [Description("Workspace-relative file path.")] string path,
        CancellationToken cancellationToken) =>
        ProposeAsync(new ToolProposal(context.SessionId, context.RunId, context.WorkspaceId, ToolAction.ReadFile, RelativePath: path), cancellationToken);

    [Description("List the top-level entries of a workspace-relative directory.")]
    private Task<string> ListDirectory(
        [Description("Workspace-relative directory path, or '.' for the root.")] string path,
        CancellationToken cancellationToken) =>
        ProposeAsync(new ToolProposal(context.SessionId, context.RunId, context.WorkspaceId, ToolAction.ListDirectory, RelativePath: path), cancellationToken);

    [Description("Search files in a workspace-relative directory for a text query.")]
    private Task<string> SearchText(
        [Description("Workspace-relative directory path.")] string path,
        [Description("Text to find.")] string query,
        CancellationToken cancellationToken) =>
        ProposeAsync(new ToolProposal(context.SessionId, context.RunId, context.WorkspaceId, ToolAction.SearchText, RelativePath: path, SearchQuery: query), cancellationToken);

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

    [Description("Run a focused command from the approved catalog in the run worktree. Requires developer approval.")]
    private Task<string> RunCommand(
        [Description("Command name from the approved catalog (for example dotnet, npm, node).")] string commandName,
        [Description("Optional command arguments.")] IReadOnlyList<string>? arguments,
        CancellationToken cancellationToken) =>
        ProposeAsync(new ToolProposal(context.SessionId, context.RunId, context.WorkspaceId, ToolAction.RunCommand, CommandName: commandName, Arguments: arguments), cancellationToken);

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
