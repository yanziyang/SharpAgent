namespace SharpAgent.Application.Abstractions;

/// <summary>Clock abstraction so time-dependent rules are deterministic in tests.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
