namespace SharpAgent.Application.Abstractions;

/// <summary>Validates a workspace root at the filesystem edge; implemented in Infrastructure.</summary>
public interface IWorkspaceRootValidator
{
    WorkspaceRootValidation Validate(string rootPath);
}

public sealed record WorkspaceRootValidation(
    bool IsValid,
    string? CanonicalRootPath,
    string? SafeMessage)
{
    public static WorkspaceRootValidation Invalid(string safeMessage) => new(false, null, safeMessage);

    public static WorkspaceRootValidation Valid(string canonicalRootPath) => new(true, canonicalRootPath, null);
}
