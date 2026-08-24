namespace SharpAgent.Domain.Changes;

public enum ChangeSetStatus
{
    Proposed = 0,
    Applied = 1,
    Failed = 2,
}

public enum FileChangeType
{
    Added = 0,
    Modified = 1,
    Deleted = 2,
}

/// <summary>A named set of proposed file changes produced by one run.</summary>
public sealed class ChangeSet
{
    private readonly List<FileChange> _files = [];

    public string Id { get; init; } = DomainId.NewChangeSetId();

    public string RunId { get; init; } = string.Empty;

    public ChangeSetStatus Status { get; internal set; } = ChangeSetStatus.Proposed;

    /// <summary>Safe summary shown in review UI.</summary>
    public string? Summary { get; internal set; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public IReadOnlyList<FileChange> Files => _files;

    internal ChangeSet()
    {
    }

    public static ChangeSet CreateNew(string runId, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("Run id is required.", nameof(runId));
        }

        return new ChangeSet { RunId = runId, CreatedAtUtc = nowUtc };
    }

    public FileChange AddFile(string relativePath, FileChangeType changeType, DateTimeOffset nowUtc)
    {
        var change = FileChange.CreateNew(Id, relativePath, changeType, nowUtc);
        _files.Add(change);
        return change;
    }

    public void MarkApplied(string? summary, DateTimeOffset nowUtc)
    {
        GuardProposed();
        Status = ChangeSetStatus.Applied;
        Summary = summary;
    }

    public void MarkFailed(string? summary, DateTimeOffset nowUtc)
    {
        GuardProposed();
        Status = ChangeSetStatus.Failed;
        Summary = summary;
    }

    private void GuardProposed()
    {
        if (Status != ChangeSetStatus.Proposed)
        {
            throw new InvalidStateTransitionException("change set", Status.ToString(), "final");
        }
    }
}

/// <summary>One changed file with hashes and bounded diff metadata.</summary>
public sealed class FileChange
{
    public string Id { get; init; } = DomainId.NewFileChangeId();

    public string ChangeSetId { get; init; } = string.Empty;

    /// <summary>Workspace-relative path (never an absolute machine path).</summary>
    public string RelativePath { get; init; } = string.Empty;

    public FileChangeType ChangeType { get; init; }

    public string? BeforeHash { get; internal set; }

    public string? AfterHash { get; internal set; }

    /// <summary>Bounded unified diff text when size permits; null for binary files.</summary>
    public string? DiffText { get; internal set; }

    public bool IsBinary { get; internal set; }

    private FileChange()
    {
    }

    internal static FileChange CreateNew(string changeSetId, string relativePath, FileChangeType changeType, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path is required.", nameof(relativePath));
        }

        return new FileChange
        {
            ChangeSetId = changeSetId,
            RelativePath = relativePath,
            ChangeType = changeType,
            IsBinary = changeType == FileChangeType.Deleted,
        };
    }
}
