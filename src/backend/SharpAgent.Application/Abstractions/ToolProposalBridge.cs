using SharpAgent.Application.Tools;

namespace SharpAgent.Application.Abstractions;

public enum ToolProposalStatus
{
    Executed = 0,
    AwaitingApproval = 1,
    Denied = 2,
    Failed = 3,
}

/// <summary>Safe outcome of one canonical tool proposal.</summary>
public sealed record ToolProposalOutcome(
    ToolProposalStatus Status,
    string? ApprovalId,
    string? OutputPreview,
    string? SafeMessage);

/// <summary>
/// The only way the runtime can reach tools: narrow facade calls become canonical
/// proposals evaluated by policy and, when required, single-use approvals. No MAF
/// tool ever touches files, Git, shell, provider configuration, or approval
/// storage directly (plan section 11.1).
/// </summary>
public interface IToolProposalBridge
{
    Task<ToolProposalOutcome> ProposeAsync(ToolProposal proposal, CancellationToken cancellationToken);
}
