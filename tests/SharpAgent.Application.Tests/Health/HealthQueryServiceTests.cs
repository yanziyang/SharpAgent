using SharpAgent.Application.Health;
using SharpAgent.TestKit.Fakes;
using Xunit;

namespace SharpAgent.Application.Tests.Health;

public sealed class HealthQueryServiceTests
{
    [Fact]
    public async Task With_no_probes_reports_healthy_application()
    {
        var service = new HealthQueryService([]);

        var snapshot = await service.ProbeAsync();

        Assert.Equal(HealthStatus.Healthy, snapshot.Overall);
        Assert.Empty(snapshot.Checks);
    }

    [Fact]
    public async Task Orders_checks_by_probe_name()
    {
        var service = new HealthQueryService(
        [
            new FakeHealthProbe("providers", HealthStatus.Degraded),
            new FakeHealthProbe("application"),
            new FakeHealthProbe("database", HealthStatus.Unready),
        ]);

        var snapshot = await service.ProbeAsync();

        Assert.Equal(["application", "database", "providers"], snapshot.Checks.Select(static c => c.Name));
    }

    [Theory]
    [InlineData(HealthStatus.Healthy, HealthStatus.Healthy, HealthStatus.Healthy)]
    [InlineData(HealthStatus.Healthy, HealthStatus.Degraded, HealthStatus.Degraded)]
    [InlineData(HealthStatus.Degraded, HealthStatus.Unready, HealthStatus.Unready)]
    public async Task Overall_status_is_the_worst_reported(
        HealthStatus first,
        HealthStatus second,
        HealthStatus expected)
    {
        var service = new HealthQueryService(
        [
            new FakeHealthProbe("a", first),
            new FakeHealthProbe("b", second),
        ]);

        var snapshot = await service.ProbeAsync();

        Assert.Equal(expected, snapshot.Overall);
    }

    [Fact]
    public async Task Throwing_probe_becomes_bounded_degraded_result_without_leaking_exception()
    {
        const string leakAttempt = "connection string Server=secret-db;Password=hunter2";
        var service = new HealthQueryService(
        [
            new FakeHealthProbe("database", () => throw new InvalidOperationException(leakAttempt)),
        ]);

        var snapshot = await service.ProbeAsync();

        var check = Assert.Single(snapshot.Checks);
        Assert.Equal(HealthStatus.Degraded, check.Status);
        Assert.Equal("Probe failed.", check.Detail);
        Assert.DoesNotContain(leakAttempt, check.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Probe_details_are_redacted_and_bounded()
    {
        const string secret = "sk-123456789012";
        var service = new HealthQueryService(
        [
            new FakeHealthProbe("provider", HealthStatus.Degraded, secret + new string('x', 400)),
        ]);

        var check = Assert.Single((await service.ProbeAsync()).Checks);

        Assert.DoesNotContain(secret, check.Detail, StringComparison.Ordinal);
        Assert.Contains("[redacted]", check.Detail, StringComparison.Ordinal);
        Assert.True(check.Detail!.Length <= 240);
    }

    [Fact]
    public async Task Continues_remaining_probes_after_a_failure()
    {
        var after = new FakeHealthProbe("z-after");
        var service = new HealthQueryService(
        [
            new FakeHealthProbe("a-throws", static () => throw new TimeoutException()),
            after,
        ]);

        var snapshot = await service.ProbeAsync();

        Assert.Equal(2, snapshot.Checks.Count);
        Assert.Equal(1, after.CallCount);
        Assert.Equal(HealthStatus.Healthy, snapshot.Checks[1].Status);
    }

    [Fact]
    public async Task Cancellation_requested_before_probes_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var probe = new FakeHealthProbe("application");
        var service = new HealthQueryService([probe]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ProbeAsync(cts.Token));

        Assert.Equal(0, probe.CallCount);
    }

    [Fact]
    public async Task Cancellation_during_probe_propagates_instead_of_degrading()
    {
        using var cts = new CancellationTokenSource();
        var pending = new TaskCompletionSource<HealthCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cts.Token.Register(() => pending.TrySetCanceled(cts.Token));
        var service = new HealthQueryService(
        [
            new FakeHealthProbe("slow", () => pending.Task),
        ]);

        var probeTask = service.ProbeAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probeTask);
    }

    [Fact]
    public async Task Blank_probe_result_name_is_replaced_with_probe_name()
    {
        var service = new HealthQueryService(
        [
            new FakeHealthProbe(
                "database",
                static () => Task.FromResult(new HealthCheckResult(string.Empty, HealthStatus.Healthy, "ok"))),
        ]);

        var snapshot = await service.ProbeAsync();

        var check = Assert.Single(snapshot.Checks);
        Assert.Equal("database", check.Name);
    }

    [Fact]
    public async Task Undefined_status_value_is_normalized_to_degraded()
    {
        var undefined = (HealthStatus)99;

        // Inject an out-of-range status directly through a custom handler.
        var service = new HealthQueryService([new WrappingProbe("weird", undefined)]);

        var snapshot = await service.ProbeAsync();

        Assert.Equal(HealthStatus.Degraded, snapshot.Checks.Single().Status);
    }

    private sealed class WrappingProbe(string name, HealthStatus overrideStatus) : IHealthProbe
    {
        public string Name => name;

        public Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new HealthCheckResult(name, overrideStatus, "raw"));
    }
}
