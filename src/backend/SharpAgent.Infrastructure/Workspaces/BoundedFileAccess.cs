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
    {
        resultsTruncated = false;
        var matches = new List<string>();

        foreach (var file in new DirectoryInfo(directory.AbsolutePath).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
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

                using var reader = new StreamReader(stream);
                content = reader.ReadToEnd();
            }
            catch (IOException)
            {
                continue;
            }

            var lines = content.Split('\n');
            for (var index = 0; index < lines.Length && matches.Count < maxResults; index++)
            {
                if (lines[index].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add($"{file.Name}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        return matches;
    }

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

