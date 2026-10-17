using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SqlHistory.Storage.Sqlite.Persistence;
using SqlHistory.Storage.Sqlite.Repositories;

namespace SqlHistory.Storage.Sqlite.Tests;

/// <summary>
/// A private in-memory SQLite database that lives as long as this object.
/// Schema is created by the real EF migrations, not EnsureCreated.
/// </summary>
public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteTestDatabase(SqliteConnection connection)
    {
        _connection = connection;
    }

    public static async Task<SqliteTestDatabase> CreateAsync(bool migrate = true)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var database = new SqliteTestDatabase(connection);
        if (migrate)
        {
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync();
        }

        return database;
    }

    public SqlHistoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlHistoryDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new SqlHistoryDbContext(options);
    }

    public StoredProcedureHistoryRepository CreateRepository()
    {
        return new StoredProcedureHistoryRepository(CreateContext());
    }

    public ValueTask DisposeAsync()
    {
        return _connection.DisposeAsync();
    }
}
