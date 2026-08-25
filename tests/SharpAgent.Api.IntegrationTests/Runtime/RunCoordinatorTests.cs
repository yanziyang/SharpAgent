using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SharpAgent.Api.IntegrationTests.TestSupport;
using SharpAgent.Api.Runtime;
using SharpAgent.Application.Abstractions;
using Xunit;

namespace SharpAgent.Api.IntegrationTests.Runtime;

/// <summary>Focused coverage for the coordinator's durable cancellation handoff.</summary>
public sealed class RunCoordinatorTests : IDisposable
{
    private readonly ApiTestHost _host = ApiTestHost.Start();

    [Fact]
    public void Request_cancellation_signals_the_active_run_token()
    {
        var coordinator = Assert.IsType<RunCoordinator>(
            _host.Factory.Services.GetRequiredService<IRunCoordinator>());
        var activeRuns = GetActiveRuns(coordinator);
        using var cancellation = new CancellationTokenSource();
        const string sessionId = "ses_active_for_test";

        Assert.True(activeRuns.TryAdd(sessionId, cancellation));
        try
        {
            coordinator.RequestCancellation(sessionId);

            Assert.True(cancellation.IsCancellationRequested);
        }
        finally
        {
            activeRuns.TryRemove(sessionId, out _);
        }
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    private static ConcurrentDictionary<string, CancellationTokenSource> GetActiveRuns(
        RunCoordinator coordinator)
    {
        var field = typeof(RunCoordinator).GetField(
            "_activeRuns",
            BindingFlags.Instance | BindingFlags.NonPublic);

        return Assert.IsType<ConcurrentDictionary<string, CancellationTokenSource>>(
            field?.GetValue(coordinator));
    }
}
