using SharpAgent.Application.Tools;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Tools;
using Xunit;

namespace SharpAgent.Application.Tests.Tools;

/// <summary>
/// Table-driven proof of the default action policy (functional spec section 14.1)
/// including Plan-mode isolation (FR-021) and operator rule overrides.
/// </summary>
public sealed class PolicyEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 21, 0, 0, TimeSpan.Zero);

    private static readonly PolicyProfile DefaultPolicy =
        PolicyProfile.Define("default-controlled", 45, 40, 5.00m, 10, Now);

    private static readonly ModelProfile ValidatedProfile = CreateValidatedProfile();

    private static ModelProfile CreateValidatedProfile()
    {
        var profile = ModelProfile.Register(ProviderKind.Fake, "P", "id", EndpointKind.None, Now);
        profile.Enable(Now);
        profile.MarkValidated(new ProfileCapabilities(true, true, null, null, null), "ok", Now);
        return profile;
    }

    private static ToolProposal Proposal(ToolAction action, string relativePath = "src/app.cs") => new(
        SessionId: "ses_1",
        RunId: "run_1",
        WorkspaceId: "ws_1",
        Action: action,
        RelativePath: action is ToolAction.ReadFile or ToolAction.ListDirectory or ToolAction.SearchText
            ? relativePath
            : null);

    public static TheoryData<ToolAction, PolicyOutcome> DefaultOutcomeTable() => new()
    {
        { ToolAction.ReadFile, PolicyOutcome.Allow },
        { ToolAction.ListDirectory, PolicyOutcome.Allow },
        { ToolAction.SearchText, PolicyOutcome.Allow },
        { ToolAction.RepositoryStatus, PolicyOutcome.Allow },
        { ToolAction.ApplyPatch, PolicyOutcome.RequireApproval },
        { ToolAction.RunCommand, PolicyOutcome.RequireApproval },
    };

    [Theory]
    [MemberData(nameof(DefaultOutcomeTable))]
    public void Execute_mode_follows_the_default_action_policy(ToolAction action, PolicyOutcome expected)
    {
        var decision = PolicyEvaluator.Evaluate(SessionMode.Execute, Proposal(action), DefaultPolicy, ValidatedProfile);

        Assert.Equal(expected, decision.Outcome);
    }

    public static TheoryData<ToolAction> ReadOnlyActionsData() => new()
    {
        { ToolAction.ReadFile },
        { ToolAction.ListDirectory },
        { ToolAction.SearchText },
        { ToolAction.RepositoryStatus },
    };

    public static TheoryData<ToolAction> SideEffectingActions() => new()
    {
        { ToolAction.ApplyPatch },
        { ToolAction.RunCommand },
    };

    [Theory]
    [MemberData(nameof(SideEffectingActions))]
    public void Plan_mode_denies_every_side_effecting_action_before_execution(ToolAction action)
    {
        var decision = PolicyEvaluator.Evaluate(SessionMode.Plan, Proposal(action), DefaultPolicy, ValidatedProfile);

        Assert.Equal(PolicyOutcome.Deny, decision.Outcome);
        Assert.Equal("plan_mode_read_only", decision.RuleMatched);
    }

    [Theory]
    [MemberData(nameof(ReadOnlyActionsData))]
    public void Plan_mode_still_allows_read_only_actions(ToolAction action)
    {
        var decision = PolicyEvaluator.Evaluate(SessionMode.Plan, Proposal(action), DefaultPolicy, ValidatedProfile);

        Assert.NotEqual(PolicyOutcome.Deny, decision.Outcome);
    }

    [Fact]
    public void Operator_rules_can_tighten_reads_to_approval()
    {
        var tightened = PolicyProfile.Define(
            "strict",
            maxRunDurationMinutes: 10,
            maxToolCalls: 5,
            maxEstimatedCostUsd: 1m,
            approvalExpiryMinutes: 5,
            Now,
            rulesJson: """{"read_file":"require_approval"}""");

        var decision = PolicyEvaluator.Evaluate(SessionMode.Execute, Proposal(ToolAction.ReadFile), tightened, ValidatedProfile);

        Assert.Equal(PolicyOutcome.RequireApproval, decision.Outcome);
        Assert.Contains(":require_approval", decision.RuleMatched, StringComparison.Ordinal);
    }

    [Fact]
    public void Operator_rules_can_deny_patch_categories_entirely()
    {
        var restrictive = PolicyProfile.Define(
            "read-only-policy",
            10, 5, 1m, 5,
            Now,
            rulesJson: """{"apply_patch":"deny"}""");

        var decision = PolicyEvaluator.Evaluate(SessionMode.Execute, Proposal(ToolAction.ApplyPatch), restrictive, ValidatedProfile);

        Assert.Equal(PolicyOutcome.Deny, decision.Outcome);
        Assert.Equal("apply_patch:operator_deny", decision.RuleMatched);
    }

    [Fact]
    public void Malformed_rule_documents_fail_closed_to_require_approval()
    {
        var broken = PolicyProfile.Define(
            "broken-rules",
            10, 5, 1m, 5,
            Now,
            rulesJson: "{not json}");

        var patchDecision = PolicyEvaluator.Evaluate(SessionMode.Execute, Proposal(ToolAction.ApplyPatch), broken, ValidatedProfile);
        Assert.Equal(PolicyOutcome.RequireApproval, patchDecision.Outcome);

        // Reads stay allowed by the built-in table even when the document is broken.
        var readDecision = PolicyEvaluator.Evaluate(SessionMode.Execute, Proposal(ToolAction.ReadFile), broken, ValidatedProfile);
        Assert.Equal(PolicyOutcome.Allow, readDecision.Outcome);
    }
}

