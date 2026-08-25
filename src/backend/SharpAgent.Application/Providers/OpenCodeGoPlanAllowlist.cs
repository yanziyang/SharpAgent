namespace SharpAgent.Application.Providers;

using SharpAgent.Domain.Profiles;

/// <summary>
/// Strict OpenCode Go Plan model allowlist (Implementation Plan section 4.2).
/// Any request for an OpenCode Go Plan model whose display name is not in this
/// list must be rejected BEFORE an outbound call.
/// </summary>
public static class OpenCodeGoPlanAllowlist
{
    public static readonly IReadOnlyList<OpenCodeGoPlanModel> ApprovedModels =
    [
        new("Ox Alpha Free", "ox-alpha-free", EndpointKind.ChatCompletions),
        new("Muse Spark 1.2 Contributor", "muse-spark-1.2-contributor", EndpointKind.Responses),
        new("MiMo-V2.5", "mimo-v2.5", EndpointKind.ChatCompletions),
    ];

    public static readonly IReadOnlyList<string> ApprovedDisplayNames =
        ApprovedModels.Select(static model => model.DisplayName).ToArray();

    public static bool IsAllowed(string displayName) =>
        ApprovedDisplayNames.Contains(displayName, StringComparer.Ordinal);

    public static string SafeMessage(string displayName) =>
        $"'{displayName}' is not an approved OpenCode Go Plan model.";

    public static OpenCodeGoPlanModel? FindByProviderModelId(string providerModelId) =>
        ApprovedModels.FirstOrDefault(model =>
            string.Equals(model.ProviderModelId, providerModelId, StringComparison.Ordinal));
}

public sealed record OpenCodeGoPlanModel(
    string DisplayName,
    string ProviderModelId,
    EndpointKind EndpointKind);
