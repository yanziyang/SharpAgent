using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Runs;

namespace SharpAgent.TestKit.Fakes;

/// <summary>
/// Scripted deterministic runtime for orchestrator tests. The handler emits
/// canonical events through the real sink so persistence paths are exercised;
/// replacements never touch the API or persistence directly.
/// </summary>
public sealed class FakeAgentRuntime : IAgentRuntime
{
    private readonly Func<RunContext, IRunEventSink, Task<RunOutcome>> _handler;

    public FakeAgentRuntime(Func<RunContext, IRunEventSink, Task<RunOutcome>>? handler = null)
    {
        _handler = handler ?? (async (_, sink) =>
        {
            await sink.EmitAsync(
                new RunEvent(
                    RunEventKind.TodoCreated,
                    Text: null,
                    TodoId: null,
                    TodoText: "Plan the change",
                    ToolName: null,
                    Detail: null,
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
            return new RunOutcome(RunStopReason.Completed, "Task complete.", 0);
        });
    }

    public List<RunContext> Contexts { get; } = [];

    public Task<RunOutcome> RunAsync(
        RunContext context,
        IRunEventSink sink,
        CancellationToken cancellationToken)
    {
        Contexts.Add(context);
        return _handler(context, sink);
    }
}

/// <summary>In-memory event sink that records every canonical event in order.</summary>
public sealed class RecordingRunEventSink : IRunEventSink
{
    public List<RunEvent> Events { get; } = [];

    public Task EmitAsync(RunEvent runEvent, CancellationToken cancellationToken)
    {
        Events.Add(runEvent);
        return Task.CompletedTask;
    }
}
