using Microsoft.EntityFrameworkCore;
using SharpAgent.Application.Abstractions;

namespace SharpAgent.Infrastructure.Persistence;

/// <summary>Unit of work over the scoped EF context: one transaction, one save.</summary>
public sealed class EfUnitOfWork(SharpAgentDbContext context) : IUnitOfWork
{
    private readonly List<Action> _afterCommit = [];
    private int _transactionDepth;

    public void RegisterAfterCommit(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _afterCommit.Add(callback);
    }

    /// <summary>Persists all tracked changes across repositories sharing this context.</summary>
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (_transactionDepth == 0)
            {
                DispatchAfterCommit();
            }
        }
        catch
        {
            _afterCommit.Clear();
            throw;
        }
    }

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            _transactionDepth++;

            try
            {
                await action(cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                _afterCommit.Clear();
                throw;
            }
            finally
            {
                _transactionDepth--;
            }

            DispatchAfterCommit();
        }).ConfigureAwait(false);
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            _transactionDepth++;
            TResult result;

            try
            {
                result = await action(cancellationToken).ConfigureAwait(false);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                _afterCommit.Clear();
                throw;
            }
            finally
            {
                _transactionDepth--;
            }

            // The callback dispatch is intentionally after the try/catch so a
            // publisher failure cannot attempt to roll back a committed DB.
            DispatchAfterCommit();
            return result;
        }).ConfigureAwait(false);
    }

    private void DispatchAfterCommit()
    {
        if (_afterCommit.Count == 0)
        {
            return;
        }

        var callbacks = _afterCommit.ToArray();
        _afterCommit.Clear();
        foreach (var callback in callbacks)
        {
            callback();
        }
    }
}
