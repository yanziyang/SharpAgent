using SharpAgent.Application.Abstractions;

namespace SharpAgent.TestKit.Fakes;

/// <summary>Settable clock for deterministic time-dependent rules.</summary>
public sealed class FakeClock(DateTimeOffset initialUtc) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = initialUtc;

    public static FakeClock At(int year, int month, int day, int hour = 10) =>
        new(new DateTimeOffset(year, month, day, hour, 0, 0, TimeSpan.Zero));

    public void Advance(TimeSpan delta) => UtcNow += delta;

    public void Set(DateTimeOffset value) => UtcNow = value;
}
