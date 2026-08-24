using Microsoft.EntityFrameworkCore;
using SharpAgent.Infrastructure.Persistence;
using SharpAgent.TestKit.Workspaces;

namespace SharpAgent.Infrastructure.Tests.Support;

/// <summary>Fresh SQLite database file per instance; migrations applied explicitly.</summary>
public sealed class SqliteTestDatabase : IDisposable
{
    public string DatabasePath { get; }

    public string ConnectionString => $"Data Source={DatabasePath}";

    public bool Initialized { get; private set; }

    public static SqliteTestDatabase Create() =>
        new(Path.Combine(TempWorkspace.Create().RootPath, "test.db"));

    /// <summary>Creates the database inside an existing directory (caller owns cleanup).</summary>
    public static SqliteTestDatabase CreateIn(string directory) =>
        new(Path.Combine(directory, "test.db"));

    private SqliteTestDatabase(string databasePath)
    {
        DatabasePath = databasePath;
    }

    public async Task InitializeAsync()
    {
        await using var context = OpenContext();
        await context.Database.MigrateAsync();
        Initialized = true;
    }

    public SharpAgentDbContext OpenContext()
    {
        var options = new DbContextOptionsBuilder<SharpAgentDbContext>()
            .UseSqlite(ConnectionString)
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        return new SharpAgentDbContext(options);
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(DatabasePath + suffix);
            }
            catch (IOException)
            {
            }
        }
    }
}
