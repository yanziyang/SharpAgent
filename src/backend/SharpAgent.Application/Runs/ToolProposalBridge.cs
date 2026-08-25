using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Tools;

namespace SharpAgent.Application.Runs;

/// <summary>
/// Bound bridge between the agent runtime's facade tools and the guarded
/// workspace tool service. Facade calls become canonical proposals; policy and
/// single-use approvals are evaluated BEFORE any executor runs.
/// </summary>
public sealed class ToolProposalBridge(WorkspaceToolService tools) : IToolProposalBridge
{
    public async Task<ToolProposalOutcome> ProposeAsync(
        ToolProposal proposal,
        CancellationToken cancellationToken)
    {
        var result = await tools.ProposeAsync(proposal, cancellationToken).ConfigureAwait(false);

        return result switch
        {
            ToolProposalResult.Executed executed => new ToolProposalOutcome(
                ToolProposalStatus.Executed,
                ApprovalId: null,
                executed.OutputPreview,
                SafeMessage: null),
            ToolProposalResult.AwaitingApproval awaiting => new ToolProposalOutcome(
                ToolProposalStatus.AwaitingApproval,
                awaiting.ApprovalId,
                OutputPreview: null,
                SafeMessage: null),
            ToolProposalResult.Denied denied => new ToolProposalOutcome(
                ToolProposalStatus.Denied,
                ApprovalId: null,
                OutputPreview: null,
                denied.Reason),
            ToolProposalResult.ModeForbidden forbidden => new ToolProposalOutcome(
                ToolProposalStatus.Denied,
                ApprovalId: null,
                OutputPreview: null,
                forbidden.Reason),
            _ => new ToolProposalOutcome(
                ToolProposalStatus.Failed,
                ApprovalId: null,
                OutputPreview: null,
                "The tool proposal failed."),
        };
    }
}
