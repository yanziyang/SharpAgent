using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharpAgent.Application.Abstractions;
using SharpAgent.Infrastructure.Persistence;
using SharpAgent.Infrastructure.Workspaces;

namespace SharpAgent.Infrastructure.Retention;

/// <summary>
/// Bounded retention settings for transient local artifacts. Audit events and change
/// evidence are deliberately not covered by these settings.
/// </summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public int WorktreeHours { get; init; } = 24;

    public int ToolOutputHours { get; init; } = 24 * 7;

    public TimeSpan WorktreeAge => TimeSpan.FromHours(WorktreeHours);

    public TimeSpan ToolOutputAge => TimeSpan.FromHours(ToolOutputHours);

    public static RetentionOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(SectionName);
        return new RetentionOptions
        {
            WorktreeHours = ReadPositiveInt(section["WorktreeHours"], 24, "WorktreeHours"),
            ToolOutputHours = ReadPositiveInt(section["ToolOutputHours"], 24 * 7, "ToolOutputHours"),
        };
    }

    private static int ReadPositiveInt(string? value, int defaultValue, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0)
        {
            return parsed;
        }

        throw new InvalidOperationException($"Retention setting '{settingName}' must be a positive integer.");
    }
}

public sealed record RetentionCleanupResult(
    int IdempotencyRecordsDeleted,
    int ToolOutputPreviewsCleared,
    int WorktreesRemoved,
    int WorktreesSkipped);

internal sealed record RegisteredWorkspaceRoot(string RootPath, string? CanonicalRootPath);

/// <summary>
/// Cleans only transient artifacts that are independently reconstructible or
/// already represented by durable audit/change evidence. Paths are constrained to
/// the dedicated managed worktree directory before the worktree service is called.
/// </summary>
public sealed class RetentionCleanupService(
    IDbContextFactory<SharpAgentDbContext> contextFactory,
    IGitWorktreeService worktrees,
    RetentionOptions options,
    ILogger<RetentionCleanupService> logger)
{
    private const string ManagedWorktreeDirectoryName = "sharpagent-worktrees";

    private static readonly Action<ILogger, int, int, int, int, Exception?> LogCleanup =
        LoggerMessage.Define<int, int, int, int>(
            LogLevel.Information,
            new EventId(1, nameof(RetentionCleanupService)),
            "Retention cleanup completed: idempotencyDeleted={IdempotencyDeleted} outputPreviewsCleared={OutputPreviewsCleared} worktreesRemoved={WorktreesRemoved} worktreesSkipped={WorktreesSkipped}");

    private static readonly Action<ILogger, string, Exception?> LogCleanupFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(RetentionCleanupService)),
            "Retention cleanup failed with {ExceptionType}; the next scheduled sweep will retry");

    public async Task<RetentionCleanupResult> CleanupAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await contextFactory
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            var idempotencyDeleted = await context.IdempotencyRecords
                .Where(record => record.ExpiresAtUtc <= nowUtc)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            var outputCutoff = nowUtc - options.ToolOutputAge;
            var outputPreviewsCleared = await context.ToolExecutions
                .Where(execution => execution.EndedAtUtc != null
                    && execution.EndedAtUtc <= outputCutoff
                    && (execution.OutputPreview != null || execution.ErrorSummary != null))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(static execution => execution.OutputPreview, static _ => (string?)null)
                        .SetProperty(static execution => execution.ErrorSummary, static _ => (string?)null),
                    cancellationToken)
                .ConfigureAwait(false);

            var worktreeResult = await CleanupWorktreesAsync(context, nowUtc, cancellationToken)
                .ConfigureAwait(false);

            var result = new RetentionCleanupResult(
                idempotencyDeleted,
                outputPreviewsCleared,
                worktreeResult.Removed,
                worktreeResult.Skipped);
            LogCleanup(
                logger,
                result.IdempotencyRecordsDeleted,
                result.ToolOutputPreviewsCleared,
                result.WorktreesRemoved,
                result.WorktreesSkipped,
                null);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Cleanup is best-effort and must not make the API unavailable. The
            // exception type is useful for diagnostics; its message may contain
            // machine paths or provider details and is intentionally omitted.
            LogCleanupFailure(logger, exception.GetType().Name, null);
            return new RetentionCleanupResult(0, 0, 0, 0);
        }
    }

    private async Task<(int Removed, int Skipped)> CleanupWorktreesAsync(
        SharpAgentDbContext context,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var cutoff = nowUtc - options.WorktreeAge;
        var registeredRoots = await context.Workspaces
            .AsNoTracking()
            .Select(static workspace => new RegisteredWorkspaceRoot(workspace.RootPath, workspace.CanonicalRootPath))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var candidates = await context.AgentRuns
            .AsNoTracking()
            .Where(run => run.EndedAtUtc != null
                && run.EndedAtUtc <= cutoff
                && run.WorktreePath != null)
            .Select(static run => new { run.ExecutionEnvironmentId, run.WorktreePath })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var removed = 0;
        var skipped = 0;
        var seenPaths = new HashSet<string>(GetPathComparer());

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryNormalizeManagedPath(candidate.WorktreePath!, out var normalizedPath)
                || !seenPaths.Add(normalizedPath)
                || IsRegisteredRootOrDescendant(normalizedPath, registeredRoots))
            {
                skipped++;
                continue;
            }

            if (!worktrees.Exists(normalizedPath))
            {
                continue;
            }

            try
            {
                await worktrees.RemoveAsync(
                        new WorktreeInfo(candidate.ExecutionEnvironmentId ?? "wt_retained", normalizedPath),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (worktrees.Exists(normalizedPath))
                {
                    skipped++;
                }
                else
                {
                    removed++;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        return (removed, skipped);
    }

    private static bool TryNormalizeManagedPath(string path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        string parent;
        string candidate;
        try
        {
            parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), ManagedWorktreeDirectoryName))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            candidate = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var relative = Path.GetRelativePath(parent, candidate);
        if (relative is "." or ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative)
            || relative.Contains(Path.DirectorySeparatorChar)
            || relative.Contains(Path.AltDirectorySeparatorChar)
            || !Path.GetFileName(candidate).StartsWith("wt_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Directory.Exists(candidate)
            && new DirectoryInfo(candidate).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        normalizedPath = candidate;
        return true;
    }

    private static bool IsRegisteredRootOrDescendant(
        string candidate,
        IReadOnlyList<RegisteredWorkspaceRoot> registeredRoots)
    {
        foreach (var root in registeredRoots)
        {
            var registered = root.CanonicalRootPath ?? root.RootPath;
            if (string.IsNullOrWhiteSpace(registered))
            {
                continue;
            }

            if (PathsOverlap(candidate, registered))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PathsOverlap(string left, string right)
    {
        string normalizedLeft;
        string normalizedRight;
        try
        {
            normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }

        return IsWithinOrEqual(normalizedLeft, normalizedRight)
            || IsWithinOrEqual(normalizedRight, normalizedLeft);
    }

    private static bool IsWithinOrEqual(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(candidate, root, comparison)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison)
            || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, comparison);
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
