namespace SharpAgent.Application.Providers;

/// <summary>
/// Strict OpenCode Go Plan model allowlist (Implementation Plan section 4.2).
/// Any request for an OpenCode Go Plan model whose display name is not in this
/// list must be rejected BEFORE an outbound call.
/// </summary>
public static class OpenCodeGoPlanAllowlist
{
    public static readonly IReadOnlyList<string> ApprovedDisplayNames =
    [
        "Ox Alpha Free",
        "Muse Spark 1.2 Contributor",
        "MiMo-V2.5",
    ];

    public static bool IsAllowed(string displayName) =>
        ApprovedDisplayNames.Contains(displayName, StringComparer.Ordinal);

    public static string SafeMessage(string displayName) =>
        $"'{displayName}' is not an approved OpenCode Go Plan model.";
}
