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

    public FileChangeType ChangeType { get; private set; }

    public string? BeforeHash { get; internal set; }

    public string? AfterHash { get; internal set; }

    /// <summary>Bounded unified diff text when size permits; null for binary files.</summary>
    public string? DiffText { get; internal set; }

    /// <summary>Bounded new content used by the MVP patch applier; binary files carry none.</summary>
    public string? AfterContentText { get; internal set; }

    public bool IsBinary { get; private set; }

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
            IsBinary = false,
        };
    }

    /// <summary>Records proposal-time evidence: hashes, bounded diff and apply payload.</summary>
    public void RecordProposalEvidence(
        string? beforeHash,
        string? afterHash,
        string? diffText,
        string? afterContentText,
        DateTimeOffset nowUtc)
    {
        if (ChangeType == FileChangeType.Deleted)
        {
            IsBinary = true; // deletions have no textual content to show
            AfterHash = null;
            DiffText = null;
            AfterContentText = null;
            BeforeHash = beforeHash ?? string.Empty;
            return;
        }

        if (afterContentText is null)
        {
            IsBinary = true;
            AfterHash = afterHash;
            DiffText = null;
            BeforeHash = beforeHash ?? string.Empty;
            return;
        }

        IsBinary = false;
        AfterHash = afterHash;
        DiffText = diffText;
        BeforeHash = beforeHash ?? string.Empty;
        AfterContentText = afterContentText.Length <= MaxContentLength
            ? afterContentText
            : throw new ArgumentException($"File content exceeds {MaxContentLength} characters.", nameof(afterContentText));
    }

    public const int MaxContentLength = 32_000;
}
