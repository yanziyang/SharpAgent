using SharpAgent.Application.Abstractions;

namespace SharpAgent.Infrastructure.Workspaces;

/// <summary>
/// Filesystem-backed root validation. Only existence and absolute-path checks happen
/// here; canonical path resolution and escape detection arrive with the workspace
/// isolation phase and re-run before every tool action (FR-002).
/// </summary>
public sealed class FileSystemRootValidator : IWorkspaceRootValidator
{
    public WorkspaceRootValidation Validate(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return WorkspaceRootValidation.Invalid("Root path is required.");
        }

        if (!Path.IsPathFullyQualified(rootPath))
        {
            return WorkspaceRootValidation.Invalid("The workspace root must be an absolute path.");
        }

        string full;
        try
        {
            full = Path.GetFullPath(rootPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return WorkspaceRootValidation.Invalid("The workspace root path is not valid.");
        }

        if (!Directory.Exists(full))
        {
            return WorkspaceRootValidation.Invalid("The workspace root directory does not exist.");
        }

        // Trailing separators would break prefix checks later; normalize once here.
        return WorkspaceRootValidation.Valid(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}
