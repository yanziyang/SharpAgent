using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Domain.Tools;

namespace SharpAgent.Application.Tools;

/// <summary>
/// Default action policy (functional spec section 14.1) evaluated BEFORE any
/// executor is reachable. Plan mode additionally denies every side-effecting
/// category outright (FR-021); unknown categories are denied by default.
/// </summary>
public sealed class PolicyEvaluator
{
    private static readonly HashSet<string> ReadOnlyActions = new(StringComparer.Ordinal)
    {
        nameof(ToolAction.ReadFile),
        nameof(ToolAction.ListDirectory),
        nameof(ToolAction.SearchText),
        nameof(ToolAction.RepositoryStatus),
    };

    private static readonly HashSet<string> ApprovalActions = new(StringComparer.Ordinal)
    {
        nameof(ToolAction.ApplyPatch),
        nameof(ToolAction.RunCommand),
    };

    /// <summary>Maps tool actions to the snake_case keys used in the operator rule document.</summary>
    private static readonly IReadOnlyDictionary<string, string> ActionToPolicyKey =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(ToolAction.ReadFile)] = "read_file",
            [nameof(ToolAction.ListDirectory)] = "list_directory",
            [nameof(ToolAction.SearchText)] = "search_text",
            [nameof(ToolAction.RepositoryStatus)] = "repo_status",
            [nameof(ToolAction.ApplyPatch)] = "apply_patch",
            [nameof(ToolAction.RunCommand)] = "run_command",
        };

    public static PolicyDecision Evaluate(
        SessionMode mode,
        ToolProposal proposal,
        PolicyProfile policy,
        ModelProfile modelProfile)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(modelProfile);

        var action = proposal.Action.ToString();
        var policyKey = ActionToPolicyKey.GetValueOrDefault(action, action);

        if (mode == SessionMode.Plan && !ReadOnlyActions.Contains(action))
        {
            return new PolicyDecision(
                PolicyOutcome.Deny,
                "plan_mode_read_only",
                "Plan mode cannot write files or run side-effecting commands.");
        }

        // Operator rules take precedence when present (tighten or relax per category);
        // without a rule, the built-in MVP table applies.
        var rules = ParseRules(policy.RulesJson);
        if (rules.TryGetValue(policyKey, out var operatorDecision))
        {
            return operatorDecision switch
            {
                "deny" => new PolicyDecision(PolicyOutcome.Deny, $"{policyKey}:operator_deny", "The operator policy denies this action."),
                "allow" => new PolicyDecision(PolicyOutcome.Allow, $"{policyKey}:operator_allow", "The operator policy allows this action without approval."),
                _ => new PolicyDecision(PolicyOutcome.RequireApproval, $"{policyKey}:require_approval", "This action requires explicit approval."),
            };
        }

        if (ReadOnlyActions.Contains(action))
        {
            return new PolicyDecision(PolicyOutcome.Allow, $"{action}:allow", "Read-only in-boundary action.");
        }

        // Unknown action categories fall through to require_approval (fail closed):
        // they must be explicitly classified before any executor is reachable.
        return new PolicyDecision(PolicyOutcome.RequireApproval, $"{action}:require_approval", "This action requires explicit approval.");
    }

    private static readonly System.Text.Json.JsonSerializerOptions RuleOptions = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static Dictionary<string, string> ParseRules(string rulesJson)
    {
        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                rulesJson,
                RuleOptions);

            return parsed ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return []; // A malformed rule document fails closed to require_approval.
        }
    }
}

