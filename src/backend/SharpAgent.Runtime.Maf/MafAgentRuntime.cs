using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Providers;

namespace SharpAgent.Runtime.Maf;

/// <summary>
/// Microsoft Agent Framework runtime adapter (plan section 11): owns harness
/// construction, facade tools, todo behavior, compaction, cancellation and the
/// canonical event translation. Provider/MAF types never escape this adapter.
/// </summary>
public sealed class MafAgentRuntime(IClock clock, ILogger<MafAgentRuntime>? logger = null) : IAgentRuntime
{
    public const int MaxAssistantSummaryLength = 2_000;
    public const int MaxOutputTokens = 2_048;

    private static readonly Action<ILogger, Exception?> LogProviderCallStarted =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(20, nameof(LogProviderCallStarted)),
            "provider_call_started");

    private static readonly Action<ILogger, Exception?> LogProviderCallCompleted =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(21, nameof(LogProviderCallCompleted)),
            "provider_call_completed");

    private static readonly Action<ILogger, string, Exception?> LogProviderCallFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(22, nameof(LogProviderCallFailed)),
            "provider_call_failed exceptionType={ExceptionType}");

    private static readonly Action<ILogger, string, Exception?> LogUnrecognizedContent =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(23, nameof(LogUnrecognizedContent)),
            "provider_update_unrecognized contentType={ContentType}");

    private sealed record ParsedTodo(string Text, bool Done);

    public async Task<RunOutcome> RunAsync(
        RunContext context,
        IRunEventSink sink,
        CancellationToken cancellationToken)
    {
        using var logScope = logger?.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["correlationId"] = context.CorrelationId,
            ["sessionId"] = context.SessionId,
            ["runId"] = context.RunId,
            ["provider"] = context.Provider,
            ["modelProfileId"] = context.ModelProfileId,
        });

        using var durationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        durationCts.CancelAfter(context.Limits.MaxDuration);
        var runCt = durationCts.Token;

        var facades = new FacadeToolRegistry(context, context.ToolBridge);

        // Compaction pipeline: collapse old tool groups, then summarize with the
        // same provider, with a truncation backstop. The notifier lets the runtime
        // emit the canonical context_compacted event when summarization ran.
        var compactionNotifier = new CompactionNotifyingChatClient(context.ChatClient);
        var compactionProvider = new CompactionProvider(new PipelineCompactionStrategy(
            new ToolResultCompactionStrategy(CompactionTriggers.MessagesExceed(8)),
            new SummarizationCompactionStrategy(compactionNotifier, CompactionTriggers.TokensExceed(0x400)),
            new TruncationCompactionStrategy(CompactionTriggers.TokensExceed(0x8000))));

        var agent = context.ChatClient
            .AsBuilder()
            .UseAIContextProviders(compactionProvider)
            .BuildAIAgent(new ChatClientAgentOptions
            {
                Name = "SharpAgent",
                ChatOptions = new ChatOptions
                {
                    Instructions = InstructionsBuilder.Build(context),
                    Tools = facades.Create().Cast<AITool>().ToList(),
                    MaxOutputTokens = MaxOutputTokens,
                },
            });

        var session = await agent.CreateSessionAsync(runCt).ConfigureAwait(false);
        var opening = BuildOpeningMessage(context);
        var processor = new UpdateProcessor(context, sink, clock, logger);
        var assistant = new StringBuilder();

        try
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Debug))
            {
                LogProviderCallStarted(logger, null);
            }

            await foreach (var update in agent.RunStreamingAsync([opening], session, new ChatClientAgentRunOptions(), runCt).ConfigureAwait(false))
            {
                var action = await processor.ProcessAsync(update, assistant).ConfigureAwait(false);
                await processor.FlushAssistantAsync(assistant).ConfigureAwait(false);
                if (action is ProcessorSignal.StopAwaitingApproval or ProcessorSignal.StopLimitReached)
                {
                    break;
                }

                if (runCt.IsCancellationRequested)
                {
                    break;
                }
            }

            await processor.FlushAssistantAsync(assistant).ConfigureAwait(false);

            if (compactionNotifier.SummarizationInvoked)
            {
                await sink.EmitAsync(
                    new RunEvent(
                        RunEventKind.ContextCompacted,
                        Text: "Context was compacted; task, todos, decisions and recent tool results were preserved.",
                        TodoId: null,
                        TodoText: null,
                        ToolName: null,
                        Detail: null,
                        clock.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }

            if (logger is not null && logger.IsEnabled(LogLevel.Debug))
            {
                LogProviderCallCompleted(logger, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new RunOutcome(RunStopReason.Cancelled, "The run was cancelled.", processor.ToolCallCount);
        }
        catch (OperationCanceledException)
        {
            return new RunOutcome(
                RunStopReason.LimitReached,
                $"The maximum run duration of {context.Limits.MaxDuration.TotalMinutes:0} minutes was reached.",
                processor.ToolCallCount);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                LogProviderCallFailed(logger, exception.GetType().Name, null);
            }

            return new RunOutcome(
                RunStopReason.ProviderError,
                SafeErrorMessage(exception),
                processor.ToolCallCount);
        }

        return processor.StopSignal switch
        {
            ProcessorSignal.StopLimitReached => new RunOutcome(
                RunStopReason.LimitReached,
                "A configured run limit was reached.",
                processor.ToolCallCount),
            ProcessorSignal.StopAwaitingApproval => new RunOutcome(
                RunStopReason.AwaitingApproval,
                "An action is awaiting developer approval.",
                processor.ToolCallCount),
            _ => new RunOutcome(
                RunStopReason.Completed,
                SafeSummary(assistant.ToString()),
                processor.ToolCallCount),
        };
    }

    private static Microsoft.Extensions.AI.ChatMessage BuildOpeningMessage(RunContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine(context.Task);
        if (!string.IsNullOrWhiteSpace(context.Instruction))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"Instruction: {context.Instruction}");
        }

        return new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, builder.ToString());
    }

    private enum ProcessorSignal
    {
        Continue = 0,
        StopAwaitingApproval = 1,
        StopLimitReached = 2,
    }

    /// <summary>Converts MAF/ExtAI update contents into canonical run events.</summary>
    private sealed class UpdateProcessor(
        RunContext context,
        IRunEventSink sink,
        IClock clock,
        ILogger<MafAgentRuntime>? logger)
    {
        public int ToolCallCount { get; private set; }

        public ProcessorSignal StopSignal { get; private set; } = ProcessorSignal.Continue;

        private string? _lastToolName;
        private string? _lastToolArguments;
        private decimal _accumulatedCostUsd;
        private bool _unknownContentLogged;
        private int _emittedAssistantLength;

        public async Task<ProcessorSignal> ProcessAsync(AgentResponseUpdate update, StringBuilder assistant)
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case ToolApprovalRequestContent approval:
                        await FlushAssistantAsync(assistant).ConfigureAwait(false);
                        await EmitProposedAsync(approval).ConfigureAwait(false);
                        StopSignal = ProcessorSignal.StopAwaitingApproval;
                        return ProcessorSignal.StopAwaitingApproval;

                    case FunctionCallContent call:
                        await FlushAssistantAsync(assistant).ConfigureAwait(false);
                        _lastToolName = call.Name;
                        _lastToolArguments = SerializeArguments(call.Arguments);
                        ToolCallCount++;
                        if (ToolCallCount > context.Limits.MaxToolCalls)
                        {
                            await EmitStatusAsync("The maximum tool-call limit was reached.").ConfigureAwait(false);
                            StopSignal = ProcessorSignal.StopLimitReached;
                            return ProcessorSignal.StopLimitReached;
                        }

                        await sink.EmitAsync(
                            new RunEvent(
                                RunEventKind.ToolStarted,
                                Text: null,
                                TodoId: null,
                                TodoText: null,
                                ToolName: SafeToolName(call.Name),
                                Detail: SafeSummary(SerializeArguments(call.Arguments)),
                                clock.UtcNow),
                            CancellationToken.None).ConfigureAwait(false);
                        break;

                    case FunctionResultContent result:
                        await HandleToolResultAsync(result, assistant).ConfigureAwait(false);
                        break;

                    case TextContent text when !string.IsNullOrWhiteSpace(text.Text):
                        AppendAssistant(assistant, text.Text);
                        break;

                    case TextReasoningContent:
                        // Hidden reasoning is never surfaced.
                        break;

                    case UsageContent usage:
                        await FlushAssistantAsync(assistant).ConfigureAwait(false);
                        var signal = await TrackUsageAsync(usage).ConfigureAwait(false);
                        if (signal == ProcessorSignal.StopLimitReached)
                        {
                            return signal;
                        }

                        break;

                    case AIContent providerText when IsTextContent(providerText):
                        // Some OpenCode-compatible transports return a TextContent
                        // instance from a compatible Microsoft.Extensions.AI
                        // assembly that does not satisfy the direct type pattern
                        // above. Read only the public text property as a bounded,
                        // provider-neutral compatibility fallback.
                        if (TryReadText(providerText, out var fallbackText))
                        {
                            AppendAssistant(assistant, fallbackText);
                        }

                        break;

                    default:
                        if (!_unknownContentLogged && logger is not null)
                        {
                            _unknownContentLogged = true;
                            LogUnrecognizedContent(logger, content.GetType().FullName ?? content.GetType().Name, null);
                        }

                        // Truly unknown content becomes a safe informational event;
                        // provider or MAF types never escape the adapter.
                        await sink.EmitAsync(
                            new RunEvent(
                                RunEventKind.Status,
                                Text: "The provider sent an unrecognized update.",
                                TodoId: null,
                                TodoText: null,
                                ToolName: null,
                                Detail: null,
                                clock.UtcNow),
                            CancellationToken.None).ConfigureAwait(false);
                        break;
                }
            }

            return ProcessorSignal.Continue;
        }

        public async Task FlushAssistantAsync(StringBuilder assistant)
        {
            if (assistant.Length <= _emittedAssistantLength)
            {
                return;
            }

            var text = assistant.ToString(_emittedAssistantLength, assistant.Length - _emittedAssistantLength);
            _emittedAssistantLength = assistant.Length;

            if (!string.IsNullOrWhiteSpace(text))
            {
                await sink.EmitAsync(
                    new RunEvent(
                        RunEventKind.AssistantSummary,
                        SafeSummary(text),
                        TodoId: null,
                        TodoText: null,
                        ToolName: null,
                        Detail: null,
                        clock.UtcNow),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task EmitProposedAsync(ToolApprovalRequestContent approval)
        {
            var name = approval.ToolCall is FunctionCallContent functionCall ? functionCall.Name : "tool";
            var arguments = approval.ToolCall is FunctionCallContent callWithArguments
                ? SafeSummary(SerializeArguments(callWithArguments.Arguments))
                : null;

            await sink.EmitAsync(
                new RunEvent(
                    RunEventKind.ToolStarted,
                    Text: null,
                    TodoId: null,
                    TodoText: null,
                    ToolName: SafeToolName(name),
                    Detail: arguments,
                    clock.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task HandleToolResultAsync(FunctionResultContent result, StringBuilder assistant)
        {
            await FlushAssistantAsync(assistant).ConfigureAwait(false);

            if (string.Equals(_lastToolName, FacadeToolRegistry.UpdateTodosToolName, StringComparison.Ordinal))
            {
                await EmitTodosAsync().ConfigureAwait(false);
            }
            else
            {
                await sink.EmitAsync(
                    new RunEvent(
                        RunEventKind.ToolOutput,
                        Text: SafeSummary(result.Result?.ToString()),
                        TodoId: null,
                        TodoText: null,
                        ToolName: SafeToolName(_lastToolName),
                        Detail: null,
                        clock.UtcNow),
                    CancellationToken.None).ConfigureAwait(false);
            }

            await sink.EmitAsync(
                new RunEvent(
                    RunEventKind.ToolCompleted,
                    Text: null,
                    TodoId: null,
                    TodoText: null,
                    ToolName: SafeToolName(_lastToolName),
                    Detail: "ok",
                    clock.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task EmitTodosAsync()
        {
            if (_lastToolArguments is null || !TryParseTodos(_lastToolArguments, out var todos))
            {
                return;
            }

            foreach (var todo in todos)
            {
                await sink.EmitAsync(
                    new RunEvent(
                        todo.Done ? RunEventKind.TodoUpdated : RunEventKind.TodoCreated,
                        Text: null,
                        TodoId: null,
                        TodoText: todo.Text,
                        ToolName: null,
                        Detail: null,
                        clock.UtcNow),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task<ProcessorSignal> TrackUsageAsync(UsageContent usage)
        {
            var input = usage.Details?.InputTokenCount;
            var output = usage.Details?.OutputTokenCount;
            if (input is not null || output is not null)
            {
                await sink.EmitAsync(
                    new RunEvent(
                        RunEventKind.UsageUpdated,
                        Text: null,
                        TodoId: null,
                        TodoText: null,
                        ToolName: null,
                        Detail: $"tokens in: {input?.ToString(CultureInfo.InvariantCulture) ?? "?"}, out: {output?.ToString(CultureInfo.InvariantCulture) ?? "?"}",
                        clock.UtcNow),
                    CancellationToken.None).ConfigureAwait(false);
            }

            if (input is not null && output is not null
                && context.Limits.InputUsdPerMillionTokens is not null
                && context.Limits.OutputUsdPerMillionTokens is not null)
            {
                _accumulatedCostUsd += UsageCostEstimator.Estimate(
                    (context.Limits.InputUsdPerMillionTokens.Value, context.Limits.OutputUsdPerMillionTokens.Value),
                    (int)input.Value,
                    (int)output.Value) ?? 0m;

                if (context.Limits.MaxEstimatedCostUsd is { } budget && _accumulatedCostUsd > budget)
                {
                    await EmitStatusAsync("The estimated cost limit was reached.").ConfigureAwait(false);
                    StopSignal = ProcessorSignal.StopLimitReached;
                    return ProcessorSignal.StopLimitReached;
                }
            }

            return ProcessorSignal.Continue;
        }

        private async Task EmitStatusAsync(string message)
        {
            await sink.EmitAsync(
                new RunEvent(
                    RunEventKind.Status,
                    Text: message,
                    TodoId: null,
                    TodoText: null,
                    ToolName: null,
                    Detail: null,
                    clock.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }

        private static bool TryParseTodos(string payload, out List<ParsedTodo> todos)
        {
            todos = [];
            try
            {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;

                // Tolerate both a bare array and a parameter-wrapped shape such as
                // {"todosJson": [...]} produced by the tool-call arguments.
                if (root.ValueKind == JsonValueKind.Object)
                {
                    var arrayProperty = root.EnumerateObject()
                        .FirstOrDefault(static property => property.Value.ValueKind == JsonValueKind.Array);
                    if (arrayProperty.Value.ValueKind != JsonValueKind.Array)
                    {
                        return false;
                    }

                    root = arrayProperty.Value;
                }

                if (root.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                foreach (var item in root.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object
                        || !item.TryGetProperty("text", out var text)
                        || text.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    var done = item.TryGetProperty("done", out var doneValue)
                               && doneValue.ValueKind == JsonValueKind.True;
                    todos.Add(new ParsedTodo(text.GetString() ?? string.Empty, done));
                }

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static void AppendAssistant(StringBuilder assistant, string delta)
        {
            if (assistant.Length >= MaxAssistantSummaryLength)
            {
                return;
            }

            var remaining = MaxAssistantSummaryLength - assistant.Length;
            assistant.Append(delta.Length <= remaining ? delta : delta[..remaining]);
        }

        private static bool IsTextContent(AIContent content) =>
            string.Equals(
                content.GetType().FullName,
                typeof(TextContent).FullName,
                StringComparison.Ordinal);

        private static bool TryReadText(AIContent content, out string text)
        {
            text = string.Empty;
            var property = content.GetType().GetProperty(
                nameof(TextContent.Text),
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public);

            if (property?.PropertyType != typeof(string)
                || property.GetValue(content) is not string value)
            {
                return false;
            }

            text = value;
            return true;
        }
    }

    private static string? SerializeArguments(IDictionary<string, object?>? arguments) =>
        arguments is null ? null : JsonSerializer.Serialize(arguments);

    private static string SafeSummary(string? message) =>
        SharpAgent.Application.Runs.RunOrchestrator.SafeSummary(message);

    private static string SafeToolName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "tool" : name;

    private static string SafeErrorMessage(Exception exception) =>
        $"The provider interrupted the run ({exception.GetType().Name}).";
}
