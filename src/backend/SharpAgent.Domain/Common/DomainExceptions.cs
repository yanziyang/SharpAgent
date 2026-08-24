namespace SharpAgent.Domain.Common;

/// <summary>Base type for safe, user-displayable domain rule violations.</summary>
public abstract class DomainException(string message) : Exception(message)
{
}

/// <summary>An aggregate transition violated its state machine.</summary>
public sealed class InvalidStateTransitionException(string entity, string current, string target)
    : DomainException($"Cannot move {entity} from '{current}' to '{target}'.")
{
    public string Entity { get; } = entity;

    public string Current { get; } = current;

    public string Target { get; } = target;
}
