using Microsoft.EntityFrameworkCore;
using SharpAgent.Infrastructure.Persistence;
using SharpAgent.Infrastructure.Setup;
using SharpAgent.Infrastructure.Tests.Support;
using Xunit;

namespace SharpAgent.Infrastructure.Tests.Persistence;

public sealed class LocalDemoSetupTests : IDisposable
{
    private readonly SqliteTestDatabase _database = SqliteTestDatabase.Create();

    [Fact]
    public async Task Enabled_setup_seeds_safe_catalog_once()
    {
        await _database.InitializeAsync();
        var seeder = new LocalDemoCatalogSeeder(CreateFactory(), new LocalDemoOptions(true));

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        await using var context = _database.OpenContext();
        var profile = Assert.Single(await context.ModelProfiles.ToListAsync());
        var policy = Assert.Single(await context.PolicyProfiles.ToListAsync());

        Assert.Equal("Offline demo (Plan only)", profile.DisplayName);
        Assert.True(profile.Enabled);
        Assert.True(profile.CanPlan());
        Assert.False(profile.CanExecute());
        Assert.Equal("Default safe policy", policy.Name);
        Assert.Equal(20, policy.MaxToolCalls);
    }

    [Fact]
    public async Task Disabled_setup_does_not_write_catalog_records()
    {
        await _database.InitializeAsync();
        var seeder = new LocalDemoCatalogSeeder(CreateFactory(), new LocalDemoOptions(false));

        await seeder.SeedAsync();

        await using var context = _database.OpenContext();
        Assert.Empty(await context.ModelProfiles.ToListAsync());
        Assert.Empty(await context.PolicyProfiles.ToListAsync());
    }

    private DbContextFactoryStub CreateFactory() => new(
        new DbContextOptionsBuilder<SharpAgentDbContext>()
            .UseSqlite(_database.ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options);

    public void Dispose() => _database.Dispose();
}
