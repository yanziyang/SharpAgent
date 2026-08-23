using System.Buffers;

namespace SharpAgent.TestKit.Workspaces;

/// <summary>
/// Disposable temporary workspace copied deterministically from
/// <c>test-assets/workspaces/sample-workspace</c>. Each instance gets a unique
/// directory under the system temp path; file contents are byte-identical between calls,
/// so tests stay deterministic while never touching real developer workspaces.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    public const string DefaultFixtureName = "sample-workspace";

    private static readonly SearchValues<char> InvalidRelativeChars = SearchValues.Create(
        ['\0', ':', '*', '?', '"', '<', '>', '|']);

    private TempWorkspace(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public static TempWorkspace Create(string fixtureName = DefaultFixtureName) =>
        CreateFrom(fixtureDirectory: FindFixtureDirectory(fixtureName));

    public static TempWorkspace CreateFrom(DirectoryInfo fixtureDirectory)
    {
        ArgumentNullException.ThrowIfNull(fixtureDirectory);

        var uniqueName = FormattableString.Invariant(
            $"sharpagent-ws-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}");
        var root = Path.Combine(Path.GetTempPath(), uniqueName);
        Directory.CreateDirectory(root);

        CopyDirectory(fixtureDirectory.FullName, root);

        return new TempWorkspace(root);
    }

    /// <summary>Writes a UTF-8 file at <paramref name="relativePath"/> inside the workspace.</summary>
    public string WriteFile(string relativePath, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ValidateRelativePath(relativePath);

        var fullPath = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents, System.Text.Encoding.UTF8);
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; temp directories are cleaned by the OS eventually.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static DirectoryInfo FindFixtureDirectory(string fixtureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureName);

        var candidate = AppContext.BaseDirectory;
        for (var current = new DirectoryInfo(candidate); current is not null; current = current.Parent)
        {
            var possible = Path.Combine(current.FullName, "test-assets", "workspaces", fixtureName);
            if (Directory.Exists(possible))
            {
                return new DirectoryInfo(possible);
            }
        }

        throw new InvalidOperationException(
            $"Test fixture workspace '{fixtureName}' was not found. Expected 'test-assets/workspaces/{fixtureName}' above the test output directory.");
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(
                Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: false);
        }
    }

    private static void ValidateRelativePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath)
            || relativePath.Split('/', '\\').Contains("..")
            || relativePath.AsSpan().ContainsAny(InvalidRelativeChars))
        {
            throw new ArgumentException("Relative workspace paths must stay inside the temporary root.", nameof(relativePath));
        }
    }
}
