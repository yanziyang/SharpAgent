using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Common;
using SharpAgent.Application.Idempotency;
using SharpAgent.Application.Security;
using SharpAgent.Domain.Workspaces;

namespace SharpAgent.Application.Workspaces;

public sealed record WorkspaceDto(
    string Id,
    string Name,
    string RootPath,
    string Status,
    string? ValidationMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record RegisterWorkspaceRequest(string Name, string RootPath);

/// <summary>
/// Operator workspace registration (FR-001). Invalid or missing roots are rejected
/// at registration time; deeper canonicalization arrives with the workspace phase.
/// </summary>
public sealed class WorkspaceService(
    IWorkspaceRepository workspaces,
    IWorkspaceRootValidator rootValidator,
    IIdempotencyStore idempotencyStore,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private IdempotencyService Idempotency { get; } = new(idempotencyStore, clock);

    public Task<WorkspaceDto> RegisterAsync(
        RegisterWorkspaceRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        RegisterCoreAsync(request, idempotencyKey, validate: true, cancellationToken);

    /// <summary>
    /// Test/operator helper that records a workspace without filesystem access.
    /// Used by deterministic test fixtures; production callers use RegisterAsync.
    /// </summary>
    public Task<WorkspaceDto> RegisterUnvalidatedAsync(
        RegisterWorkspaceRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        RegisterCoreAsync(request, idempotencyKey, validate: false, cancellationToken);

    private async Task<WorkspaceDto> RegisterCoreAsync(
        RegisterWorkspaceRequest request,
        string idempotencyKey,
        bool validate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!validate)
        {
            return await RegisterPersistedAsync(request, validatedRoot: null, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
        }

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Display name is required."];
        }

        if (string.IsNullOrWhiteSpace(request.RootPath))
        {
            errors["rootPath"] = ["Root path is required."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var precheck = rootValidator.Validate(request.RootPath);
        if (!precheck.IsValid)
        {
            // FR-001: an invalid or missing root cannot be saved at all.
            throw ValidationException.ForField("rootPath", precheck.SafeMessage ?? "The workspace root is not usable.");
        }

        return await RegisterPersistedAsync(request, precheck.CanonicalRootPath, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<WorkspaceDto> RegisterPersistedAsync(
        RegisterWorkspaceRequest request,
        string? validatedRoot,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var requestHash = IdempotencyService.HashPayload(request);

        var result = await Idempotency.ExecuteAsync(
            unitOfWork,
            idempotencyKey,
            OperationNames.RegisterWorkspace,
            requestHash,
            async transactionCancellationToken =>
            {
                var now = clock.UtcNow;
                var workspace = Domain.Workspaces.Workspace.Register(
                    request.Name.Trim(), request.RootPath, now);

                if (validatedRoot is not null)
                {
                    workspace.MarkValidated(validatedRoot, now);
                }
                else
                {
                    workspace.MarkUnavailable("Not validated yet.", now);
                }

                await workspaces.AddAsync(workspace, transactionCancellationToken).ConfigureAwait(false);
                return Project(workspace);
            },
            cancellationToken).ConfigureAwait(false);

        return result.Value;
    }

    public async Task<IReadOnlyList<WorkspaceDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var list = await workspaces.ListAsync(cancellationToken).ConfigureAwait(false);
        return [.. list.OrderBy(static workspace => workspace.Name, StringComparer.OrdinalIgnoreCase).Select(Project)];
    }

    public static WorkspaceDto Project(Workspace workspace) => new(
        workspace.Id,
        workspace.Name,
        workspace.RootPath,
        workspace.Status.ToString(),
        SecretRedactor.Redact(workspace.ValidationMessage),
        workspace.CreatedAtUtc,
        workspace.UpdatedAtUtc);
}


