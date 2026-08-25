using System.Security.Cryptography;
using System.Text;
using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Security;
using SharpAgent.Application.Tools;

namespace SharpAgent.Infrastructure.Workspaces;

/// <summary>
/// Bounded, redacted file access. Output caps are enforced here so no unbounded
/// content can ever reach the model or the browser (FR-035).
/// </summary>
public sealed class BoundedFileAccess : IWorkspaceFileAccess
{
    public bool FileExists(ResolvedTarget target) => File.Exists(target.AbsolutePath);

    public bool DirectoryExists(ResolvedTarget target) => Directory.Exists(target.AbsolutePath);

    public (string Content, bool Truncated) ReadTextBounded(ResolvedTarget target, int maxCharacters)
    {
        var text = File.ReadAllText(target.AbsolutePath);
        if (text.Length <= maxCharacters)
        {
            return (SecretRedactor.Redact(text)!, false);
        }

        return (SecretRedactor.Redact(text[..maxCharacters])!, true);
    }

    public void WriteText(ResolvedTarget target, string contents)
    {
        var directory = Path.GetDirectoryName(target.AbsolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(target.AbsolutePath, contents, Encoding.UTF8);
    }

    public void DeleteFile(ResolvedTarget target) => File.Delete(target.AbsolutePath);

    public IReadOnlyList<(string Name, long Length, bool IsDirectory)> ListTopLevel(ResolvedTarget directory)
    {
        var result = new List<(string, long, bool)>();
        var dir = new DirectoryInfo(directory.AbsolutePath);

        foreach (var child in dir.EnumerateDirectories())
        {
            result.Add((child.Name, 0, true));
        }

        foreach (var file in dir.EnumerateFiles())
        {
            result.Add((file.Name, file.Length, false));
        }

        return result.OrderBy(static entry => entry.Item1, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> SearchText(
        ResolvedTarget directory,
        string query,
        int maxResults,
        out bool resultsTruncated)
        => SearchTextCore(directory, query, maxResults, recursive: false, out resultsTruncated);

    public IReadOnlyList<string> SearchTextRecursive(
        ResolvedTarget directory,
        string query,
        int maxResults,
        out bool resultsTruncated)
        => SearchTextCore(directory, query, maxResults, recursive: true, out resultsTruncated);

    public IReadOnlyList<string> FindFiles(
        ResolvedTarget directory,
        string namePattern,
        int maxResults,
        out bool resultsTruncated)
    {
        resultsTruncated = false;
        var matches = new List<string>();
        var pattern = string.IsNullOrWhiteSpace(namePattern) ? "*" : namePattern.Trim();

        try
        {
            foreach (var file in new DirectoryInfo(directory.AbsolutePath).EnumerateFiles(pattern, SafeEnumerationOptions))
            {
                if (matches.Count >= maxResults)
                {
                    resultsTruncated = true;
                    break;
                }

                matches.Add(ToWorkspaceRelativePath(directory, file.FullName));
            }
        }
        catch (ArgumentException)
        {
            // Invalid wildcard syntax is treated as no matches; the caller still
            // receives a bounded, non-sensitive result.
        }
        catch (IOException)
        {
            // A disappearing or inaccessible subtree is not allowed to escape the
            // workspace tool boundary as an unbounded filesystem error.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return matches;
    }

    private static readonly EnumerationOptions SafeEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    private static readonly EnumerationOptions TopLevelEnumerationOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    private static List<string> SearchTextCore(
        ResolvedTarget directory,
        string query,
        int maxResults,
        bool recursive,
        out bool resultsTruncated)
    {
        resultsTruncated = false;
        var matches = new List<string>();

        try
        {
            var enumerationOptions = recursive ? SafeEnumerationOptions : TopLevelEnumerationOptions;
            foreach (var file in new DirectoryInfo(directory.AbsolutePath).EnumerateFiles("*", enumerationOptions))
            {
                if (matches.Count >= maxResults)
                {
                    resultsTruncated = true;
                    break;
                }

                string content;
                try
                {
                    // Skip obvious binaries by null sniffing the first chunk.
                    using var stream = file.OpenRead();
                    var buffer = new byte[1024];
                    var read = stream.Read(buffer, 0, buffer.Length);
                    if (read > 0 && Array.IndexOf(buffer, (byte)0, 0, read) >= 0)
                    {
                        continue;
                    }

                    // The null sniff consumed the leading bytes; rewind before reading
                    // the full content, otherwise small files appear empty.
                    stream.Position = 0;

                    using var reader = new StreamReader(stream);
                    content = reader.ReadToEnd();
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                var relativePath = ToWorkspaceRelativePath(directory, file.FullName);
                var lines = content.Split('\n');
                for (var index = 0; index < lines.Length && matches.Count < maxResults; index++)
                {
                    if (lines[index].Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add($"{relativePath}:{index + 1}: {lines[index].Trim()}");
                    }
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return matches;
    }

    private static string ToWorkspaceRelativePath(ResolvedTarget directory, string absolutePath) =>
        Path.GetRelativePath(directory.AbsolutePath, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    public string? FileHash(ResolvedTarget target)
    {
        if (!File.Exists(target.AbsolutePath))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(target.AbsolutePath);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}

