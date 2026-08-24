namespace SharpAgent.Domain.Policies;

/// <summary>
/// Operator-managed run limits and tool policy. Per-request overrides may only
/// tighten these values, never relax them (functional spec section 10.2).
/// </summary>
public sealed class PolicyProfile
{
    public string Id { get; init; } = DomainId.NewPolicyProfileId();

    public string Name { get; init; } = string.Empty;

    /// <summary>JSON rule document (tool category -> decision) evaluated by policy engine.</summary>
    public string RulesJson { get; internal set; } = "{}";

    public int MaxRunDurationMinutes { get; internal set; }

    public int MaxToolCalls { get; internal set; }

    public decimal MaxEstimatedCostUsd { get; internal set; }

    public int ApprovalExpiryMinutes { get; internal set; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; internal set; }

    private PolicyProfile()
    {
    }

    public static PolicyProfile Define(
        string name,
        int maxRunDurationMinutes,
        int maxToolCalls,
        decimal maxEstimatedCostUsd,
        int approvalExpiryMinutes,
        DateTimeOffset nowUtc,
        string? rulesJson = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Policy name is required.", nameof(name));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxRunDurationMinutes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxToolCalls, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxEstimatedCostUsd, decimal.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(approvalExpiryMinutes, 1);

        return new PolicyProfile
        {
            Name = name,
            MaxRunDurationMinutes = maxRunDurationMinutes,
            MaxToolCalls = maxToolCalls,
            MaxEstimatedCostUsd = maxEstimatedCostUsd,
            ApprovalExpiryMinutes = approvalExpiryMinutes,
            RulesJson = rulesJson ?? DefaultRulesJson,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }

    /// <summary>Default controlled MVP rules (functional spec section 14.1).</summary>
    public const string DefaultRulesJson =
        """{"read_file":"allow","list_directory":"allow","search_text":"allow","repo_status":"allow","apply_patch":"require_approval","write_file":"require_approval","run_command":"require_approval","run_tests":"require_approval","delete":"deny","move":"deny","install":"deny","publish":"deny","git_commit":"deny","network":"deny"}""";

    /// <summary>Returns the tightened override or the policy value when the override is absent.</summary>
    public int ApplyDurationOverride(int? requestedMinutes) =>
        requestedMinutes is null ? MaxRunDurationMinutes : Math.Min(requestedMinutes.Value, MaxRunDurationMinutes);

    public int ApplyToolCallOverride(int? requestedMaxToolCalls) =>
        requestedMaxToolCalls is null ? MaxToolCalls : Math.Min(requestedMaxToolCalls.Value, MaxToolCalls);

    public decimal ApplyCostOverride(decimal? requestedMaxCostUsd) =>
        requestedMaxCostUsd is null ? MaxEstimatedCostUsd : Math.Min(requestedMaxCostUsd.Value, MaxEstimatedCostUsd);
}
