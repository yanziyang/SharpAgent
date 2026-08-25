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
            // Windows PowerShell 5.1 is present on Windows 11. PowerShell 7 and
            // Git Bash can be provided by PATH; both remain approval-gated and
            // execute only through the hardened server-side process runner. The
            // inline command sets are deliberately tiny: these tools are
            // adapters for focused diagnostics, not a general-purpose terminal.
            ["powershell"] = new(
                "powershell.exe",
                ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command"],
                new HashSet<string>(
                    ["Get-Date -Format o", "Get-Location", "$PSVersionTable.PSVersion.ToString()"],
                    StringComparer.Ordinal)),
            ["bash"] = new(
                "bash.exe",
                ["-lc"],
                new HashSet<string>(["pwd", "bash --version"], StringComparer.Ordinal)),
        });

    public bool TryResolve(string commandName, out FocusedCommandTemplate template)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            throw new ArgumentException("Command name is required.", nameof(commandName));
        }

        return _commands.TryGetValue(commandName, out template!);
    }

    public bool TryResolve(
        string commandName,
        IReadOnlyList<string>? arguments,
        out FocusedCommandTemplate template)
    {
        if (!TryResolve(commandName, out template))
        {
            return false;
        }

        return template.Accepts(arguments ?? []);
    }
}

public sealed record FocusedCommandTemplate(
    string Executable,
    IReadOnlyList<string> BaseArguments,
    IReadOnlySet<string>? AllowedInlineCommands = null)
{
    public bool Accepts(IReadOnlyList<string> arguments)
    {
        if (AllowedInlineCommands is null)
        {
            return true;
        }

        return arguments.Count == 1 && AllowedInlineCommands.Contains(arguments[0]);
    }
}

/// <summary>Immutable payload persisted with an approval enabling exact re-execution.</summary>
public sealed record ApprovalStoredPayload(
    ToolProposal Proposal,
    IReadOnlyList<ResolvedTarget> Targets,
    string PatchContentHash);

