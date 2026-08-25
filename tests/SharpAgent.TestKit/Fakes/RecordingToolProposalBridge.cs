using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Tools;

namespace SharpAgent.TestKit.Fakes;

/// <summary>Records every bridge proposal; scripts outcomes per tool name.</summary>
public sealed class RecordingToolProposalBridge : IToolProposalBridge
{
    private Func<ToolProposal, ToolProposalOutcome> _handler;

    public RecordingToolProposalBridge(Func<ToolProposal, ToolProposalOutcome>? handler = null)
    {
        _handler = handler ?? (static _ => new ToolProposalOutcome(
            ToolProposalStatus.Executed,
            ApprovalId: null,
            "ok",
            SafeMessage: null));
    }

    /// <summary>Swappable outcome policy, e.g. to script denials per tool.</summary>
    public Func<ToolProposal, ToolProposalOutcome> Handler
    {
        get => _handler;
        set => _handler = value ?? throw new ArgumentNullException(nameof(value));
    }

    public List<ToolProposal> Proposals { get; } = [];

    public Task<ToolProposalOutcome> ProposeAsync(
        ToolProposal proposal,
        CancellationToken cancellationToken)
    {
        Proposals.Add(proposal);
        return Task.FromResult(_handler(proposal));
    }
}
