using System.Text.Json;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Providers;
using SharpAgent.Domain.Profiles;
using SharpAgent.Providers;
using SharpAgent.Providers.Common;
using Xunit;

namespace SharpAgent.LiveProviderTests;

/// <summary>
/// Local opt-in OpenCode Go Plan smoke validation (Implementation Plan 4.2-4.3).
/// Exactly three approved display names; the parameter rows are guarded so the
/// runner rejects any extra model. Results land in a LOCAL IGNORED report with
/// safe metadata only — never uploaded, never containing secrets or raw payloads.
/// </summary>
public sealed class OpenCodeGoLiveSmokeTests
{
    public const string ProviderModelIdsVariable = "SHARPAGENT_OPENCODE_GO_PROVIDER_MODEL_IDS";

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };

    /// <summary>The ONLY parameter rows allowed. Exactly the three allowlisted names.</summary>
    public static IEnumerable<object[]> ApprovedModels()
    {
        foreach (var displayName in OpenCodeGoPlanAllowlist.ApprovedDisplayNames)
        {
            yield return [displayName];
        }
    }

    [Fact]
    public void Approved_model_rows_are_exactly_the_three_allowlisted_names()
    {
        var rows = ApprovedModels().Select(static row => (string)row[0]).ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(
            OpenCodeGoPlanAllowlist.ApprovedDisplayNames.Order(),
            rows.Order());
        Assert.Equal(rows.Distinct().Count(), rows.Count);
    }

    [LiveProviderTheory]
    [MemberData(nameof(ApprovedModels))]
    public async Task Approved_plan_models_validate_without_side_effects(string displayName)
    {
        var providerModelId = ResolveProviderModelId(displayName)
            ?? throw new InvalidOperationException(
                $"Live smoke requires {ProviderModelIdsVariable} to map '{displayName}' to a provider model id.");

        var profile = ModelProfile.Register(
            ProviderKind.OpenCodeGo,
            displayName,
            providerModelId,
            EndpointKind.ChatCompletions,
            Now);

        var adapter = new OpenCodeGoAdapter(new ProviderValidationRunner(
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) }));

        var result = await adapter.ValidateAsync(
            profile,
            new ProviderSecretReference(LiveProviderFactAttribute.ApiKeyVariable),
            CancellationToken.None);

        // Plan 4.3: a sanitized provider failure (quota, availability, model error)
        // is a VALID reported outcome; the report must stay safe either way.
        Assert.True(result.Error.SafeMessage.Length <= ProviderErrorMapper.MaxErrorMessageLength);
        Assert.DoesNotContain(
            Environment.GetEnvironmentVariable(LiveProviderFactAttribute.ApiKeyVariable) ?? string.Empty,
            result.Error.SafeMessage,
            StringComparison.Ordinal);

        AppendReportEntry(displayName, providerModelId, result);
    }

    private static string? ResolveProviderModelId(string displayName)
    {
        var configured = Environment.GetEnvironmentVariable(ProviderModelIdsVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        var ids = configured.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var index = OpenCodeGoPlanAllowlist.ApprovedDisplayNames
            .Select((name, position) => (name, position))
            .FirstOrDefault(pair => pair.name == displayName).position;

        return index < ids.Length ? ids[index] : null;
    }

    /// <summary>Appends one safe metadata row to the local ignored report.</summary>
    private static void AppendReportEntry(string displayName, string providerModelId, ProfileValidationResult result)
    {
        var reportPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "live-provider-report.json");
        var fullPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var entries = File.Exists(fullPath)
            ? JsonSerializer.Deserialize<List<ReportEntry>>(File.ReadAllText(fullPath)) ?? []
            : [];

        entries.Add(new ReportEntry(
            OccurredAtUtc: DateTimeOffset.UtcNow,
            DisplayName: displayName,
            Streaming: result.Streaming,
            ToolCalling: result.ToolCalling,
            LatencyMs: result.LatencyMs,
            Outcome: result.Error.Category == ProviderErrorCategory.None ? "passed" : "sanitized-failure",
            ErrorCategory: result.Error.Category.ToString()));

        File.WriteAllText(fullPath, JsonSerializer.Serialize(entries, ReportOptions));
    }

    private sealed record ReportEntry(
        DateTimeOffset OccurredAtUtc,
        string DisplayName,
        bool Streaming,
        bool ToolCalling,
        long LatencyMs,
        string Outcome,
        string ErrorCategory);
}
