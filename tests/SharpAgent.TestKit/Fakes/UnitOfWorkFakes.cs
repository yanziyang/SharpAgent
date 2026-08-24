using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Common;

namespace SharpAgent.TestKit.Fakes;

/// <summary>Pass-through unit of work; application tests do not exercise real transactions.</summary>
public sealed class PassThroughUnitOfWork : IUnitOfWork
{
    public int SaveCalls { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCalls++;
        return Task.CompletedTask;
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await action(cancellationToken).ConfigureAwait(false);
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        var result = await action(cancellationToken).ConfigureAwait(false);
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }
}

/// <summary>Configurable workspace-root validator without filesystem access.</summary>
public sealed class StubRootValidator(string? canonicalRootPath = null, bool isValid = true) : IWorkspaceRootValidator
{
    public static StubRootValidator ValidFor(string canonical) => new(canonical, isValid: true);

    public static StubRootValidator Invalid(string message = "Root directory is missing.") =>
        new(null, isValid: false);

    public string? Message { get; set; } = isValid ? null : "Root directory is missing.";

    public WorkspaceRootValidation Validate(string rootPath) => isValid
        ? WorkspaceRootValidation.Valid(canonicalRootPath ?? rootPath)
        : WorkspaceRootValidation.Invalid(Message ?? "The workspace root is not usable.");
}
