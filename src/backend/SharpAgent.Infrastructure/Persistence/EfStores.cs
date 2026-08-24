using Microsoft.EntityFrameworkCore;
using SharpAgent.Application.Abstractions;
using SharpAgent.Domain.Idempotency;
using SharpAgent.Domain.Policies;
using SharpAgent.Domain.Profiles;
using SharpAgent.Domain.Sessions;

namespace SharpAgent.Infrastructure.Persistence;

public sealed class EfModelProfileRepository(SharpAgentDbContext context) : IModelProfileRepository
{
    public Task<ModelProfile?> FindAsync(string modelProfileId, CancellationToken cancellationToken) =>
        context.ModelProfiles.FirstOrDefaultAsync(profile => profile.Id == modelProfileId, cancellationToken);

    public async Task AddAsync(ModelProfile profile, CancellationToken cancellationToken) =>
        await context.ModelProfiles.AddAsync(profile, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ModelProfile>> ListAsync(CancellationToken cancellationToken)
    {
        var list = await context.ModelProfiles
            .AsNoTracking()
            .OrderBy(static profile => profile.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return list.AsReadOnly();
    }
}

public sealed class EfPolicyProfileRepository(SharpAgentDbContext context) : IPolicyProfileRepository
{
    public Task<PolicyProfile?> FindAsync(string policyProfileId, CancellationToken cancellationToken) =>
        context.PolicyProfiles.FirstOrDefaultAsync(policy => policy.Id == policyProfileId, cancellationToken);

    public async Task AddAsync(PolicyProfile profile, CancellationToken cancellationToken) =>
        await context.PolicyProfiles.AddAsync(profile, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<PolicyProfile>> ListAsync(CancellationToken cancellationToken)
    {
        var list = await context.PolicyProfiles
            .AsNoTracking()
            .OrderBy(static policy => policy.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return list.AsReadOnly();
    }
}

public sealed class EfRunLeaseRepository(SharpAgentDbContext context) : IRunLeaseRepository
{
    public async Task AddAsync(RunLease lease, CancellationToken cancellationToken) =>
        await context.RunLeases.AddAsync(lease, cancellationToken).ConfigureAwait(false);

    public Task<RunLease?> FindActiveBySessionAsync(string sessionId, CancellationToken cancellationToken) =>
        context.RunLeases.FirstOrDefaultAsync(
            lease => lease.SessionId == sessionId && lease.ReleasedAtUtc == null,
            cancellationToken);

    public async Task ReleaseForRunAsync(string runId, DateTimeOffset releasedAtUtc, CancellationToken cancellationToken)
    {
        var leases = await context.RunLeases
            .Where(lease => lease.RunId == runId && lease.ReleasedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var lease in leases)
        {
            lease.Release(releasedAtUtc);
        }
    }

    public async Task<IReadOnlyList<RunLease>> FindUnreleasedAsync(CancellationToken cancellationToken)
    {
        var list = await context.RunLeases
            .Where(lease => lease.ReleasedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return list.AsReadOnly();
    }
}

public sealed class EfIdempotencyStore(SharpAgentDbContext context) : IIdempotencyStore
{
    public Task<IdempotencyRecord?> FindAsync(string key, CancellationToken cancellationToken) =>
        context.IdempotencyRecords.FirstOrDefaultAsync(record => record.Key == key, cancellationToken);

    public async Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken) =>
        await context.IdempotencyRecords.AddAsync(record, cancellationToken).ConfigureAwait(false);

    public async Task<int> DeleteExpiredAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken) =>
        await context.IdempotencyRecords
            .Where(record => record.ExpiresAtUtc <= cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
}
