using SharpAgent.Application.Abstractions;

namespace SharpAgent.Infrastructure.Timing;

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
