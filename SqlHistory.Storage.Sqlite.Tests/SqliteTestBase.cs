using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SqlHistory.Storage.Sqlite.Persistence;

namespace SqlHistory.Storage.Sqlite.Tests;

public class SqliteTestBase : IAsyncLifetime
{
    private string _databasePath = null!;

    protected SqlHistoryDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SqlHistoryDbContext>().UseSqlite($"Data Source={_databasePath}")
            .Options;

        return new SqlHistoryDbContext(options);
    }

    public async Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"sqlhistory-test-{Guid.NewGuid():N}.db");

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }


    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }
}