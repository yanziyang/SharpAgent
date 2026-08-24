namespace SharpAgent.Domain.Usage;

/// <summary>
/// Per-run provider usage captured when the adapter reports it. Estimated cost is
/// derived from profile capability metadata, never from secrets (FR-063).
/// </summary>
public sealed class UsageRecord
{
    public string Id { get; init; } = DomainId.NewUsageId();

    public string RunId { get; init; } = string.Empty;

    public string SessionId { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string ModelProfileId { get; init; } = string.Empty;

    public long? InputTokens { get; internal set; }

    public long? OutputTokens { get; internal set; }

    public decimal? EstimatedCostUsd { get; internal set; }

    public long? LatencyMs { get; internal set; }

    public int ContextCompactions { get; internal set; }

    public int ToolCalls { get; internal set; }

    public DateTimeOffset RecordedAtUtc { get; internal set; }

    private UsageRecord()
    {
    }

    public static UsageRecord StartNew(
        string runId,
        string sessionId,
        string provider,
        string modelProfileId,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("Run id is required.", nameof(runId));
        }

        return new UsageRecord
        {
            RunId = runId,
            SessionId = sessionId,
            Provider = provider,
            ModelProfileId = modelProfileId,
            RecordedAtUtc = nowUtc,
        };
    }

    public void Record(long? inputTokens, long? outputTokens, decimal? estimatedCostUsd, long? latencyMs, DateTimeOffset nowUtc)
    {
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        EstimatedCostUsd = estimatedCostUsd;
        LatencyMs = latencyMs;
        RecordedAtUtc = nowUtc;
    }
}
