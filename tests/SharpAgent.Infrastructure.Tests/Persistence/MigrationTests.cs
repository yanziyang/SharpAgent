using SharpAgent.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SharpAgent.Infrastructure.Tests.Support;
using SharpAgent.TestKit.Workspaces;
using Xunit;

namespace SharpAgent.Infrastructure.Tests.Persistence;

public sealed class MigrationTests
{
    [Fact]
    public async Task Fresh_database_applies_migrations_idempotently()
    {
        using var database = SqliteTestDatabase.Create();

        await database.InitializeAsync();
        Assert.True(File.Exists(database.DatabasePath));

        // Second run must be a no-op (committed migrations, design section 4.3).
        await using var context = database.OpenContext();
        var exception = await Record.ExceptionAsync(
            () => context.Database.MigrateAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Sqlite_pragmas_are_applied_on_open_connections()
    {
        using var database = SqliteTestDatabase.Create();
        await database.InitializeAsync();

        await using var connection = new SqliteConnection(database.ConnectionString);
        await connection.OpenAsync();

        using (var pragmaCommand = connection.CreateCommand())
        {
            pragmaCommand.CommandText = "PRAGMA journal_mode;";
            var mode = (string?)(await pragmaCommand.ExecuteScalarAsync());
            Assert.Equal("wal", mode?.ToLowerInvariant());
        }

        await using var verifyingContext = database.OpenContext();
        await verifyingContext.Database.OpenConnectionAsync();
        using (var foreignKeys = verifyingContext.Database.GetDbConnection().CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_keys;";
            await verifyingContext.Database.OpenConnectionAsync();
            var enabled = (long?)(await foreignKeys.ExecuteScalarAsync());
            Assert.Equal(1L, enabled);
        }
    }

    [Fact]
    public void Relative_database_paths_are_resolved_and_directories_created()
    {
        using var workspace = TempWorkspace.Create();
        var configuredPath = Path.Combine(workspace.RootPath, "nested", "app.db");

        var resolved = DatabasePath.Resolve(configuredPath);

        Assert.True(Path.IsPathRooted(resolved));
        Assert.True(Directory.Exists(Path.GetDirectoryName(resolved)));
    }
}

