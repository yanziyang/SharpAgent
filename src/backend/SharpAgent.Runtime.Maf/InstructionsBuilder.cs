using System.Globalization;
using System.Text;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Sessions;

namespace SharpAgent.Runtime.Maf;

/// <summary>
/// Builds bounded, mode-specific system instructions. Never contains secrets,
/// raw provider payloads, or unrestricted environment values (plan 11.1).
/// </summary>
public static class InstructionsBuilder
{
    public const int MaxTaskCharacters = 4_000;

    public static string Build(RunContext context)
    {
        var builder = new StringBuilder();

        builder.AppendLine("You are SharpAgent, a trusted-local coding agent.");
        builder.AppendLine("You work inside an isolated workspace. You may only use the tools provided to you.");
        builder.AppendLine("Never claim an action succeeded unless a tool reported it.");
        builder.AppendLine("Keep every reply concise: summarize intent and results, never reveal hidden reasoning.");

        if (context.Mode == SessionMode.Plan)
        {
            builder.AppendLine(
                "You are in PLAN mode: propose a plan and maintain it with the todo tools. "
                + "You can read and search the workspace, but you must NOT modify files, apply patches, "
                + "or run side-effecting commands. Side-effecting tools are not available to you.");
        }
        else
        {
            builder.AppendLine(
                "You are in EXECUTE mode. Maintain a visible todo plan before high-impact actions. "
                + "Patches and focused commands require a developer approval; wait for the approval "
                + "instead of working around it.");
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"Task: {Truncate(context.Task, MaxTaskCharacters)}");
        if (!string.IsNullOrWhiteSpace(context.Instruction))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"Follow-up instruction: {Truncate(context.Instruction, MaxTaskCharacters)}");
        }

        if (context.RetainedTodos.Count > 0)
        {
            builder.AppendLine("Retained todos from earlier runs:");
            foreach (var todo in context.RetainedTodos.Take(50))
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {Truncate(todo, 200)}");
            }
        }

        if (!string.IsNullOrWhiteSpace(context.CompactedHistorySummary))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"Prior work summary: {Truncate(context.CompactedHistorySummary, MaxTaskCharacters)}");
        }

        if (context.DecisionsSummary.Count > 0)
        {
            builder.AppendLine("Actions currently awaiting developer decisions:");
            foreach (var decision in context.DecisionsSummary.Take(5))
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {Truncate(decision, 200)}");
            }
        }

        return builder.ToString();
    }

    private static string Truncate(string text, int max)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
