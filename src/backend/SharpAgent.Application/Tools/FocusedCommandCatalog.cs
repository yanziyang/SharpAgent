using SharpAgent.Application.Common;

namespace SharpAgent.Application.Tools;

/// <summary>
/// The only commands executable through SharpAgent: a small operator-defined catalog
/// (FR-033). Browser/runtime input can never name an arbitrary executable — only a
/// catalog key plus extra arguments appended to the fixed template.
/// </summary>
public sealed class FocusedCommandCatalog
{
    private readonly IReadOnlyDictionary<string, FocusedCommandTemplate> _commands;

    public FocusedCommandCatalog(IReadOnlyDictionary<string, FocusedCommandTemplate> commands)
    {
        _commands = commands;
    }

    public static FocusedCommandCatalog Default { get; } = new(
        new Dictionary<string, FocusedCommandTemplate>(StringComparer.Ordinal)
        {
            ["dotnet"] = new("dotnet", []),
            ["npm"] = new("npm", []),
            ["node"] = new("node", []),
        });

    public bool TryResolve(string commandName, out FocusedCommandTemplate template)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            throw new ArgumentException("Command name is required.", nameof(commandName));
        }

        return _commands.TryGetValue(commandName, out template!);
    }
}

public sealed record FocusedCommandTemplate(string Executable, IReadOnlyList<string> BaseArguments);

/// <summary>Immutable payload persisted with an approval enabling exact re-execution.</summary>
public sealed record ApprovalStoredPayload(
    ToolProposal Proposal,
    IReadOnlyList<ResolvedTarget> Targets,
    string PatchContentHash);

