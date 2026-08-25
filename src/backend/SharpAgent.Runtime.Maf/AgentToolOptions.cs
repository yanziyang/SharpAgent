using Microsoft.Extensions.Configuration;

namespace SharpAgent.Runtime.Maf;

/// <summary>
/// Server-side tool exposure controls. The browser never supplies this list;
/// operators configure it locally and the runtime still applies mode, policy,
/// approval, workspace and run-limit checks to every invocation.
/// </summary>
public sealed record AgentToolOptions(
    IReadOnlySet<string> EnabledTools,
    IReadOnlySet<string> DisabledTools)
{
    public const string SectionName = "AgentTools";

    public static AgentToolOptions Default { get; } = new(
        new HashSet<string>(StringComparer.Ordinal)
        {
            FacadeToolRegistry.ReadToolName,
            FacadeToolRegistry.WriteToolName,
            FacadeToolRegistry.EditToolName,
            FacadeToolRegistry.BashToolName,
            FacadeToolRegistry.PowerShellToolName,
            FacadeToolRegistry.GrepToolName,
            FacadeToolRegistry.FindToolName,
            FacadeToolRegistry.LsToolName,
            FacadeToolRegistry.UpdateTodosToolName,
            FacadeToolRegistry.RepositoryStatusToolName,
            FacadeToolRegistry.ApplyPatchToolName,
            FacadeToolRegistry.RunCommandToolName,
        },
        new HashSet<string>(StringComparer.Ordinal));

    public bool IsEnabled(string toolName) =>
        (EnabledTools.Count == 0 || EnabledTools.Contains(toolName))
        && !DisabledTools.Contains(toolName);

    public static AgentToolOptions FromConfiguration(IConfiguration? configuration)
    {
        if (configuration is null)
        {
            return Default;
        }

        var section = configuration.GetSection(SectionName);
        var enabled = ReadNames(section.GetSection("Enabled"));
        var disabled = ReadNames(section.GetSection("Disabled"));

        return new AgentToolOptions(enabled, disabled);
    }

    private static HashSet<string> ReadNames(IConfigurationSection section) =>
        new HashSet<string>(
            section.GetChildren()
                .Select(static child => child.Value?.Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!),
            StringComparer.Ordinal);
}
