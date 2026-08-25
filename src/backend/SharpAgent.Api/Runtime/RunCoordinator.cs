using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Common;
using SharpAgent.Application.Runs;

namespace SharpAgent.Api.Runtime;

/// <summary>
/// In-process run queue for the trusted-local MVP. A queue item contains the
/// immutable run identity created by the start transaction, so an idempotent
/// HTTP retry cannot execute a second run.
/// </summary>
public sealed class RunCoordinator(
    IServiceScopeFactory scopeFactory,
    ILogger<RunCoordinator> logger) : BackgroundService, IRunCoordinator
{
    private static readonly Action<ILogger, string, string, string, Exception?> StaleRunLog =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Debug,
            new EventId(5101, nameof(StaleRunLog)),
            "Skipping stale run queue item {RunId} for session {SessionId}: {Code}");

    private static readonly Action<ILogger, string, string, Exception?> FailedRunLog =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(5102, nameof(FailedRunLog)),
            "Run execution failed for session {SessionId}, run {RunId}");

    private static readonly Action<ILogger, string, string, Exception?> RecoveryFailedLog =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(5103, nameof(RecoveryFailedLog)),
            "Could not persist interruption for session {SessionId}, run {RunId}");

    private readonly Channel<RunWorkItem> _queue = Channel.CreateUnbounded<RunWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly ConcurrentDictionary<string, byte> _queuedRuns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeRuns = new(StringComparer.Ordinal);

    public async ValueTask QueueAsync(RunWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        if (!_queuedRuns.TryAdd(workItem.RunId, 0))
        {
            return;
        }

        try
        {
            await _queue.Writer.WriteAsync(workItem, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _queuedRuns.TryRemove(workItem.RunId, out _);
            throw;
        }
    }

    public void RequestCancellation(string sessionId)
    {
        if (_activeRuns.TryGetValue(sessionId, out var cancellation))
        {
            cancellation.Cancel();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var workItem in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await ExecuteOneAsync(workItem, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown is cooperative; startup recovery handles any lease
            // that did not reach a terminal persistence transition.
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    private async Task ExecuteOneAsync(RunWorkItem workItem, CancellationToken stoppingToken)
    {
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _activeRuns[workItem.SessionId] = runCancellation;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<RunOrchestrator>();
            await orchestrator.RunAsync(workItem.SessionId, workItem.RunId, runCancellation.Token)
                .ConfigureAwait(false);
        }
        catch (ConflictException exception) when (exception.Code is "no_active_run" or "run_not_active")
        {
            StaleRunLog(logger, workItem.RunId, workItem.SessionId, exception.Code, null);
        }
        catch (ConflictException exception) when (exception.Code == "unsupported_provider")
        {
            // A profile can be present in a test or operator database before its
            // adapter is installed. Keep the run cancellable rather than racing
            // the caller with an asynchronous terminal transition.
            FailedRunLog(logger, workItem.SessionId, workItem.RunId, exception);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown is not a user-visible run result.
        }
        catch (Exception exception)
        {
            // The durable run remains recoverable and is moved to interrupted
            // before the queue continues with another item.
            if (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var recoveryScope = scopeFactory.CreateAsyncScope();
                    await recoveryScope.ServiceProvider
                        .GetRequiredService<RunOrchestrator>()
                        .InterruptAfterFailureAsync(workItem.SessionId, workItem.RunId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception recoveryException)
                {
                    RecoveryFailedLog(logger, workItem.SessionId, workItem.RunId, recoveryException);
                }
            }

            FailedRunLog(logger, workItem.SessionId, workItem.RunId, exception);
        }
        finally
        {
            if (_activeRuns.TryGetValue(workItem.SessionId, out var current)
                && ReferenceEquals(current, runCancellation))
            {
                _activeRuns.TryRemove(workItem.SessionId, out _);
            }
        }
    }
}

public static class RunCoordinatorServiceCollectionExtensions
{
    public static IServiceCollection AddRunCoordinator(this IServiceCollection services)
    {
        services.AddSingleton<RunCoordinator>();
        services.AddSingleton<IRunCoordinator>(static serviceProvider =>
            serviceProvider.GetRequiredService<RunCoordinator>());
        services.AddSingleton<IHostedService>(static serviceProvider =>
            serviceProvider.GetRequiredService<RunCoordinator>());
        return services;
    }
}
