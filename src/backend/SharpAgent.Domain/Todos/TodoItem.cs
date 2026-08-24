namespace SharpAgent.Domain.Todos;

public enum TodoStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
}

/// <summary>
/// One visible plan item. Sequence is unique per session/run; status changes are
/// represented in audit events, never by mutating history (technical design 4.2).
/// </summary>
public sealed class TodoItem
{
    public string Id { get; init; } = DomainId.NewTodoId();

    public string SessionId { get; init; } = string.Empty;

    public string RunId { get; init; } = string.Empty;

    public int Sequence { get; init; }

    public string Text { get; internal set; } = string.Empty;

    public TodoStatus Status { get; internal set; }

    public DateTimeOffset UpdatedAtUtc { get; internal set; }

    internal TodoItem()
    {
    }

    public static TodoItem Create(string sessionId, string runId, int sequence, string text, DateTimeOffset nowUtc)
    {
        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "Sequence is one-based.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Todo text is required.", nameof(text));
        }

        return new TodoItem
        {
            SessionId = sessionId,
            RunId = runId,
            Sequence = sequence,
            Text = text,
            Status = TodoStatus.Pending,
            UpdatedAtUtc = nowUtc,
        };
    }

    public void UpdateText(string text, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Todo text is required.", nameof(text));
        }

        Text = text;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Todos may move freely between visible states — replanning re-opens completed
    /// items; the audit trail records every transition (design section 4.2).
    /// </summary>
    public void TransitionTo(TodoStatus target, DateTimeOffset nowUtc)
    {
        if (Status != target)
        {
            Status = target;
            UpdatedAtUtc = nowUtc;
        }
    }
}
