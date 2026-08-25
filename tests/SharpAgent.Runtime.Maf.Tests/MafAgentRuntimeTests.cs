using Microsoft.Extensions.AI;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Providers;
using SharpAgent.Application.Tools;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;
using SharpAgent.Runtime.Maf;
using SharpAgent.TestKit.Fakes;
using Xunit;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace SharpAgent.Runtime.Maf.Tests;

/// <summary>
/// MAF adapter contracts (plan 11.2): deterministic scripted chat client drives
/// the real Agent Framework harness; assertions cover canonical events, approval
/// gating, plan-mode safety, limits, cancellation and provider types not escaping.
/// </summary>
public sealed class MafAgentRuntimeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeChatClient _chat = new();
    private readonly RecordingToolProposalBridge _bridge = new();
    private readonly RecordingRunEventSink _sink = new();
    private readonly MafAgentRuntime _runtime = new(new FakeClock(Now));

    public void Dispose() => _chat.Dispose();

    private RunContext Context(
        SessionMode mode,
        int maxToolCalls = 10,
        TimeSpan? maxDuration = null,
        decimal? maxCost = null,
        string? instruction = null,
        IReadOnlyList<string>? decisions = null) => new(
        SessionId: "s1",
        RunId: "r1",
        WorkspaceId: "w1",
        WorkspaceRootPath: @"C:\workspace",
        WorktreePath: null,
        Mode: mode,
        Task: "Implement the sample feature.",
        Instruction: instruction,
        ChatClient: _chat,
        ToolBridge: _bridge,
        Limits: new RunLimits(
            maxToolCalls,
            maxDuration ?? TimeSpan.FromMinutes(30),
            maxCost,
            InputUsdPerMillionTokens: 0.50m,
            OutputUsdPerMillionTokens: 1.50m),
        RetainedTodos: ["Retained todo one"],
        CompactedHistorySummary: "A prior run planned the approach.",
        DecisionsSummary: decisions ?? []);

    [Fact]
    public async Task Plan_mode_emits_todos_and_safe_summaries_without_side_effects()
    {
        _chat.Step(
            FakeChatClient.Text("Let me inspect the workspace first."),
            FakeChatClient.Call("update_todos", """{"todosJson":[{"text":"Inspect structure","done":false},{"text":"Summarize findings","done":false}]}"""));
        _chat.Step(FakeChatClient.Text("The workspace contains a single src file."));

        var outcome = await _runtime.RunAsync(Context(SessionMode.Plan), _sink, CancellationToken.None);

        Assert.Equal(RunStopReason.Completed, outcome.StopReason);
        Assert.Equal(
            ["Let me inspect the workspace first.", "The workspace contains a single src file."],
            _sink.Events.Where(static runEvent => runEvent.Kind == RunEventKind.AssistantSummary)
                .Select(static runEvent => runEvent.Text));

        var created = _sink.Events.Where(static runEvent => runEvent.Kind == RunEventKind.TodoCreated).ToList();
        Assert.Equal(2, created.Count);
        Assert.Equal("Inspect structure", created[0].TodoText);
        Assert.Equal("Summarize findings", created[1].TodoText);

        Assert.DoesNotContain(_sink.Events, static runEvent => runEvent.ToolName == "apply_patch");
        Assert.DoesNotContain(_bridge.Proposals, static proposal => proposal.Action == ToolAction.ApplyPatch);
    }

    [Fact]
    public async Task Plan_mode_cannot_invoke_side_effect_facades_even_when_proposed()
    {
        _chat.Step(FakeChatClient.Call("apply_patch", """{"changeSetId":"cs_1"}"""));

        var outcome = await _runtime.RunAsync(Context(SessionMode.Plan), _sink, CancellationToken.None);

        Assert.True(
            outcome.StopReason is RunStopReason.Completed or RunStopReason.ProviderError,
            $"Expected a safe outcome, got {outcome.StopReason}");
        Assert.DoesNotContain(_bridge.Proposals, static proposal => proposal.Action == ToolAction.ApplyPatch);
        Assert.DoesNotContain(_bridge.Proposals, static proposal => proposal.Action == ToolAction.RunCommand);
    }

    [Fact]
    public async Task Execute_mode_high_impact_actions_follow_a_visible_todo_and_stop_for_approval()
    {
        _chat.Step(
            FakeChatClient.Text("Here is the plan."),
            FakeChatClient.Call("update_todos", """{"todosJson":[{"text":"Apply the fix","done":false}]}"""));
        _chat.Step(FakeChatClient.Call("apply_patch", """{"changeSetId":"cs_1"}"""));

        var outcome = await _runtime.RunAsync(Context(SessionMode.Execute), _sink, CancellationToken.None);

        Assert.Equal(RunStopReason.AwaitingApproval, outcome.StopReason);

        var todoIndex = _sink.Events.FindIndex(static runEvent => runEvent.Kind == RunEventKind.TodoCreated);
        var toolIndex = _sink.Events.FindIndex(static runEvent => runEvent.ToolName == "apply_patch");
        Assert.True(todoIndex >= 0 && toolIndex > todoIndex, "The visible todo must precede the high-impact action.");

        Assert.Empty(_bridge.Proposals);
    }

    [Fact]
    public async Task Tool_call_limit_stops_the_run_with_a_status_event()
    {
        _chat.Step(FakeChatClient.Call("read", """{"path":"src/a.cs"}"""));
        _chat.Step(FakeChatClient.Call("read", """{"path":"src/b.cs"}"""));

        foreach (var e in _sink.Events) { Console.WriteLine($"DBG {e.Kind} tool={e.ToolName} text={e.Text}"); }
        var outcome = await _runtime.RunAsync(Context(SessionMode.Execute, maxToolCalls: 1), _sink, CancellationToken.None);
        foreach (var e in _sink.Events) { Console.WriteLine($"DBG {e.Kind} tool={e.ToolName} text={e.Text}"); }

        Assert.Equal(RunStopReason.LimitReached, outcome.StopReason);
        Assert.Contains(_sink.Events, static runEvent =>
            runEvent.Kind == RunEventKind.Status && runEvent.Text!.Contains("tool-call", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duration_limit_stops_a_slow_provider()
    {
        _chat.Step(FakeChatClient.Text("starting"), FakeChatClient.Delay(500));
        _chat.Step(FakeChatClient.Text("still running"), FakeChatClient.Delay(500));

        var outcome = await _runtime.RunAsync(
            Context(SessionMode.Plan, maxDuration: TimeSpan.FromMilliseconds(150)),
            _sink,
            CancellationToken.None);

        Assert.Equal(RunStopReason.LimitReached, outcome.StopReason);
    }

    [Fact]
    public async Task Provider_errors_become_safe_failures()
    {
        _chat.Step(FakeChatClient.Text("boom"));
        var throwing = new ThrowingChatClient();

        var context = Context(SessionMode.Plan) with { ChatClient = throwing };
        var outcome = await _runtime.RunAsync(context, _sink, CancellationToken.None);

        Assert.Equal(RunStopReason.ProviderError, outcome.StopReason);
        Assert.DoesNotContain("boom", outcome.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_returns_cancelled()
    {
        _chat.Step(FakeChatClient.Text("starting"), FakeChatClient.Delay(500));
        using var cts = new CancellationTokenSource(100);

        var outcome = await _runtime.RunAsync(Context(SessionMode.Plan), _sink, cts.Token);

        Assert.Equal(RunStopReason.Cancelled, outcome.StopReason);
    }

    [Fact]
    public async Task Reasoning_content_is_never_surfaced()
    {
        _chat.Step(new TextReasoningContent("hidden chain of thought"));

        var outcome = await _runtime.RunAsync(Context(SessionMode.Plan), _sink, CancellationToken.None);

        Assert.Equal(RunStopReason.Completed, outcome.StopReason);
        Assert.DoesNotContain("hidden chain of thought", _sink.Events.Select(static runEvent => runEvent.Text ?? string.Empty), StringComparer.Ordinal);
        Assert.DoesNotContain(_sink.Events, static runEvent => runEvent.Kind == RunEventKind.AssistantSummary);
    }

    [Fact]
    public async Task Unknown_content_becomes_a_safe_informational_event()
    {
        _chat.Step(new UnknownContent());

        var outcome = await _runtime.RunAsync(Context(SessionMode.Plan), _sink, CancellationToken.None);

        Assert.Equal(RunStopReason.Completed, outcome.StopReason);
        Assert.Contains(_sink.Events, static runEvent =>
            runEvent.Kind == RunEventKind.Status && runEvent.Text!.Contains("unrecognized", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Usage_updates_emit_usage_events()
    {
        _chat.Step(FakeChatClient.Text("done"), FakeChatClient.Usage(1_000, 500));

        var outcome = await _runtime.RunAsync(Context(SessionMode.Plan), _sink, CancellationToken.None);

        Assert.Equal(RunStopReason.Completed, outcome.StopReason);
        Assert.Contains(_sink.Events, static runEvent =>
            runEvent.Kind == RunEventKind.UsageUpdated && runEvent.Detail!.Contains("in: 1000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cost_limit_stops_the_run()
    {
        _chat.Step(FakeChatClient.Text("expensive"), FakeChatClient.Usage(100_000_000, 100_000_000));

        var outcome = await _runtime.RunAsync(
            Context(SessionMode.Plan, maxCost: 1m),
            _sink,
            CancellationToken.None);

        Assert.Equal(RunStopReason.LimitReached, outcome.StopReason);
    }

    [Fact]
    public async Task Compaction_notifier_flags_when_the_summarizer_is_invoked()
    {
        var inner = new FakeChatClient().Step(FakeChatClient.Text("summary"));
        var notifier = new CompactionNotifyingChatClient(inner);

        Assert.False(notifier.SummarizationInvoked);
        var response = await notifier.GetResponseAsync([new ChatMessage(ChatRole.User, "summarize")]);

        Assert.True(notifier.SummarizationInvoked);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task Read_only_facades_execute_through_the_bridge()
    {
        _chat.Step(FakeChatClient.Call("read", """{"path":"src/a.cs"}"""));
        _chat.Step(FakeChatClient.Call("ls", """{"path":"."}"""));
        _chat.Step(FakeChatClient.Call("grep", """{"path":".","query":"needle"}"""));
        _chat.Step(FakeChatClient.Call("repository_status", "{}"));
        _chat.Step(FakeChatClient.Text("done"));

        var outcome = await _runtime.RunAsync(Context(SessionMode.Plan), _sink, CancellationToken.None);

        Assert.Equal(RunStopReason.Completed, outcome.StopReason);
        Assert.Equal(
            [ToolAction.ReadFile, ToolAction.ListDirectory, ToolAction.SearchText, ToolAction.RepositoryStatus],
            _bridge.Proposals.Select(static proposal => proposal.Action));
        Assert.Contains(_sink.Events, static runEvent =>
            runEvent.Kind == RunEventKind.ToolCompleted && runEvent.ToolName == "repository_status");
    }

    [Fact]
    public async Task Run_command_requires_approval_in_execute_mode()
    {
        _chat.Step(FakeChatClient.Call("run_command", """{"commandName":"dotnet","arguments":["test"]}"""));

        var outcome = await _runtime.RunAsync(Context(SessionMode.Execute), _sink, CancellationToken.None);

        Assert.Equal(RunStopReason.AwaitingApproval, outcome.StopReason);
        Assert.Empty(_bridge.Proposals);
        Assert.Contains(_sink.Events, static runEvent => runEvent.ToolName == "run_command");
    }

    [Fact]
    public async Task Denied_facades_return_safe_bounded_text()
    {
        _bridge.Handler = proposal => proposal.Action == ToolAction.ReadFile
            ? new ToolProposalOutcome(ToolProposalStatus.Denied, null, null, "Reads are not allowed in this workspace.")
            : new ToolProposalOutcome(ToolProposalStatus.Executed, null, "ok", null);

        _chat.Step(FakeChatClient.Call("read", """{"path":"src/a.cs"}"""));
        _chat.Step(FakeChatClient.Text("finished"));

        var outcome = await _runtime.RunAsync(Context(SessionMode.Plan), _sink, CancellationToken.None);

        Assert.Equal(RunStopReason.Completed, outcome.StopReason);
        Assert.Contains(_sink.Events, static runEvent =>
            runEvent.Kind == RunEventKind.ToolOutput
             && runEvent.Text!.Contains("not permitted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failed_facades_return_a_safe_fallback_text()
    {
        _bridge.Handler = static _ => new ToolProposalOutcome(
            ToolProposalStatus.Failed,
            ApprovalId: null,
            OutputPreview: null,
            SafeMessage: null);
        _chat.Step(FakeChatClient.Call("read", """{"path":"src/a.cs"}"""));

        var outcome = await _runtime.RunAsync(Context(SessionMode.Plan), _sink, CancellationToken.None);

        Assert.Equal(RunStopReason.Completed, outcome.StopReason);
        Assert.Contains(_sink.Events, static runEvent =>
            runEvent.Kind == RunEventKind.ToolOutput
            && runEvent.Text!.Contains("Action failed: unknown error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_todos_facade_validates_well_formed_and_malformed_payloads()
    {
        var function = Assert.Single(
            new FacadeToolRegistry(Context(SessionMode.Plan), _bridge).Create(),
            static tool => tool.Name == FacadeToolRegistry.UpdateTodosToolName);

        var valid = await function.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["todosJson"] = """[{"text":"Inspect","done":false},{"text":"Finish","done":true}]""",
            }),
            CancellationToken.None);
        Assert.Equal("ok", valid?.ToString());

        foreach (var payload in new[]
        {
            "not-json",
            "{}",
            "[42]",
            "[{}]",
            """[{"text":42,"done":false}]""",
            """[{"text":"missing done"}]""",
            """[{"text":"wrong done","done":"false"}]""",
        })
        {
            var result = await function.InvokeAsync(
                new AIFunctionArguments(new Dictionary<string, object?>
                {
                    ["todosJson"] = payload,
                }),
                CancellationToken.None);
            Assert.Equal("error: todos must be a JSON array of {text, done} objects.", result?.ToString());
        }
    }

    [Fact]
    public void Pi_style_tool_surface_is_mode_and_configuration_aware()
    {
        var planNames = new FacadeToolRegistry(Context(SessionMode.Plan), _bridge)
            .Create()
            .Select(static tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(planNames.SetEquals(
            [
                FacadeToolRegistry.ReadToolName,
                FacadeToolRegistry.LsToolName,
                FacadeToolRegistry.GrepToolName,
                FacadeToolRegistry.FindToolName,
                FacadeToolRegistry.UpdateTodosToolName,
                FacadeToolRegistry.RepositoryStatusToolName,
            ]));
        Assert.DoesNotContain(FacadeToolRegistry.WriteToolName, planNames);
        Assert.DoesNotContain(FacadeToolRegistry.PowerShellToolName, planNames);

        var executeNames = new FacadeToolRegistry(Context(SessionMode.Execute), _bridge)
            .Create()
            .Select(static tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(FacadeToolRegistry.WriteToolName, executeNames);
        Assert.Contains(FacadeToolRegistry.EditToolName, executeNames);
        Assert.Contains(FacadeToolRegistry.BashToolName, executeNames);
        Assert.Contains(FacadeToolRegistry.PowerShellToolName, executeNames);
        Assert.Contains(FacadeToolRegistry.ApplyPatchToolName, executeNames);

        var filtered = new AgentToolOptions(
            new HashSet<string>([FacadeToolRegistry.ReadToolName, FacadeToolRegistry.PowerShellToolName], StringComparer.Ordinal),
            new HashSet<string>([FacadeToolRegistry.PowerShellToolName], StringComparer.Ordinal));
        var filteredNames = new FacadeToolRegistry(Context(SessionMode.Execute), _bridge, filtered)
            .Create()
            .Select(static tool => tool.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(filteredNames.SetEquals([FacadeToolRegistry.ReadToolName]));
    }

    [Fact]
    public async Task Malformed_todo_payloads_are_ignored_safely()
    {
        _chat.Step(FakeChatClient.Call("update_todos", """{"not":"todos"}"""));
        _chat.Step(FakeChatClient.Call("update_todos", """{"todosJson":[{"done":true}]}"""));
        _chat.Step(FakeChatClient.Call("update_todos", """{"todosJson":42}"""));

        var outcome = await _runtime.RunAsync(Context(SessionMode.Plan), _sink, CancellationToken.None);

        Assert.Equal(RunStopReason.Completed, outcome.StopReason);
        Assert.DoesNotContain(_sink.Events, static runEvent => runEvent.Kind == RunEventKind.TodoCreated);
        Assert.DoesNotContain(_sink.Events, static runEvent => runEvent.Kind == RunEventKind.TodoUpdated);
    }

    [Fact]
    public async Task Assistant_summaries_are_bounded()
    {
        var longDelta = new string('x', 3_000);
        _chat.Step(FakeChatClient.Text(longDelta));

        var outcome = await _runtime.RunAsync(Context(SessionMode.Plan), _sink, CancellationToken.None);

        Assert.Equal(RunStopReason.Completed, outcome.StopReason);
        var summary = Assert.Single(_sink.Events, static runEvent => runEvent.Kind == RunEventKind.AssistantSummary);
        Assert.True(summary.Text!.Length <= MafAgentRuntime.MaxAssistantSummaryLength);
    }

    [Fact]
    public async Task Opening_message_includes_the_instruction()
    {
        _chat.Step(FakeChatClient.Text("ok"));

        await _runtime.RunAsync(Context(SessionMode.Plan, instruction: "Focus on tests only."), _sink, CancellationToken.None);

        var opening = _chat.LastMessages!.First(static message => message.Role == ChatRole.User);
        Assert.Contains("Focus on tests only.", opening.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compaction_notifier_reports_streaming_summarization()
    {
        var inner = new FakeChatClient().Step(FakeChatClient.Text("summary"));
        var notifier = new CompactionNotifyingChatClient(inner);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in notifier.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "summarize")]))
        {
            updates.Add(update);
        }

        Assert.True(notifier.SummarizationInvoked);
        Assert.NotEmpty(updates);
    }

    [Fact]
    public void Compaction_notifier_delegates_service_lookup_and_disposal()
    {
        var inner = new TrackingChatClient();
        var notifier = new CompactionNotifyingChatClient(inner);

        Assert.Same(inner.Service, notifier.GetService(typeof(object)));

        notifier.Dispose();

        Assert.True(inner.Disposed);
    }

    [Fact]
    public async Task Assistant_summary_stops_appending_after_the_bound()
    {
        _chat.Step(
            FakeChatClient.Text(new string('x', MafAgentRuntime.MaxAssistantSummaryLength)),
            FakeChatClient.Text("ignored after the bound"));

        var outcome = await _runtime.RunAsync(Context(SessionMode.Plan), _sink, CancellationToken.None);

        Assert.Equal(RunStopReason.Completed, outcome.StopReason);
        var summary = Assert.Single(_sink.Events, static runEvent => runEvent.Kind == RunEventKind.AssistantSummary);
        Assert.Equal(MafAgentRuntime.MaxAssistantSummaryLength, summary.Text!.Length);
    }

    [Fact]
    public void Instructions_reflect_mode_and_retained_state()
    {
        var planInstructions = InstructionsBuilder.Build(Context(
            SessionMode.Plan,
            instruction: "Focus on the todo flow.",
            decisions: ["Apply change set cs_1"]));
        Assert.Contains("PLAN mode", planInstructions, StringComparison.Ordinal);
        Assert.Contains("Retained todo one", planInstructions, StringComparison.Ordinal);
        Assert.Contains("Prior work summary", planInstructions, StringComparison.Ordinal);
        Assert.Contains("Focus on the todo flow.", planInstructions, StringComparison.Ordinal);
        Assert.Contains("awaiting developer decisions", planInstructions, StringComparison.Ordinal);
        Assert.Contains("Apply change set cs_1", planInstructions, StringComparison.Ordinal);

        var executeInstructions = InstructionsBuilder.Build(Context(SessionMode.Execute));
        Assert.Contains("EXECUTE mode", executeInstructions, StringComparison.Ordinal);
        Assert.Contains("developer approval", executeInstructions, StringComparison.Ordinal);

        var boundedInstructions = InstructionsBuilder.Build(
            Context(SessionMode.Plan, instruction: new string('x', InstructionsBuilder.MaxTaskCharacters + 1)));
        Assert.Contains(new string('x', InstructionsBuilder.MaxTaskCharacters), boundedInstructions, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', InstructionsBuilder.MaxTaskCharacters + 1), boundedInstructions, StringComparison.Ordinal);
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("boom");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("boom");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class TrackingChatClient : IChatClient
    {
        public object Service { get; } = new();

        public bool Disposed { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => Service;

        public void Dispose() => Disposed = true;
    }

    private sealed class UnknownContent : AIContent
    {
    }
}
