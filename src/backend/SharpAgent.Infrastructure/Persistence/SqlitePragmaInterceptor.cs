using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SharpAgent.Infrastructure.Persistence;

/// <summary>Applies the documented SQLite pragmas on every new connection.</summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Execute(connection, "PRAGMA journal_mode=WAL;");
        Execute(connection, "PRAGMA foreign_keys=ON;");
        Execute(connection, "PRAGMA busy_timeout=5000;");
        Execute(connection, "PRAGMA synchronous=NORMAL;");
    }

    private static void Execute(DbConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}
