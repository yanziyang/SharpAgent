using SharpAgent.Application.Abstractions;
using SharpAgent.Application.Common;

namespace SharpAgent.TestKit.Fakes;

/// <summary>Pass-through unit of work; application tests do not exercise real transactions.</summary>
public sealed class PassThroughUnitOfWork : IUnitOfWork
{
    private readonly List<Action> _afterCommit = [];

    public int SaveCalls { get; private set; }

    public void RegisterAfterCommit(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _afterCommit.Add(callback);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCalls++;
        DispatchAfterCommit();
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

    private void DispatchAfterCommit()
    {
        var callbacks = _afterCommit.ToArray();
        _afterCommit.Clear();
        foreach (var callback in callbacks)
        {
            callback();
        }
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
