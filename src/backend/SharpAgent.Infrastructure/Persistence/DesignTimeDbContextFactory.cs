using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SharpAgent.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for the EF tooling (migrations). Reads SHARPAGENT_SQLITE_PATH
/// when set so the quality gate can target a scratch database.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Design-time tooling entry point; exercised by 'dotnet ef' in the migration verification gate, not by runtime tests.")]
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SharpAgentDbContext>
{
    public static string DefaultDatabasePath { get; } = Path.Combine("data", "sharpagent.db");

    public SharpAgentDbContext CreateDbContext(string[] args)
    {
        var configured = Environment.GetEnvironmentVariable(InfrastructureOptions.SqlitePathVariable)
                         ?? DefaultDatabasePath;

        var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath.Resolve(configured),
        }.ToString();

        var builder = new DbContextOptionsBuilder<SharpAgentDbContext>();
        builder.UseSqlite(connectionString);

        return new SharpAgentDbContext(builder.Options);
    }
}

public static class DatabasePath
{
    /// <summary>Returns an absolute path; relative paths resolve under the current working directory.</summary>
    public static string Resolve(string configuredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);

        var absolute = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(configuredPath);

        var directory = Path.GetDirectoryName(absolute);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return absolute;
    }
}
