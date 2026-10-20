using Microsoft.EntityFrameworkCore;
using SqlHistory.Core.Models;
using SqlHistory.Storage.Sqlite.Repositories;

namespace SqlHistory.Storage.Sqlite.Tests.Repositories;

public class StoredProcedureHistoryRepositoryTests: SqliteTestBase
{
    
    [Fact]
    public async Task MigrateAsync_ShouldBeIdempotent()
    {
        await using var dbContext =
            CreateDbContext();

        await dbContext.Database.MigrateAsync();

        await dbContext.Database.MigrateAsync();
    }
    
    [Fact]
    public async Task GetLatestVersionAsync_ShouldReturnNull_WhenNoVersionsExist()
    {
        await using var dbContext = CreateDbContext();
        
        var repository = new StoredProcedureHistoryRepository(dbContext);

        var result = await repository.GetLatestVersionAsync("dbo", "GetUsers");
        
        
        Assert.Null(result);
    }
    
    
    [Fact]
    public async Task AddVersionAsync_ShouldPreserveCapturedAtUtc()
    {
        // Arrange
        var expected =
            new DateTime(
                2026, 10, 20,
                8, 30, 0,
                DateTimeKind.Utc);

        var snapshot =
            CreateSnapshot() with
            {
                CapturedAtUtc = expected
            };

        await using var dbContext =
            CreateDbContext();

        var repository =
            new StoredProcedureHistoryRepository(dbContext);

        // Act
        await repository.AddVersionAsync(snapshot);

        var loaded =
            await repository.GetLatestVersionAsync(
                snapshot.SchemaName,
                snapshot.ProcedureName);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(
            expected,
            loaded.CapturedAtUtc);
    }
    
    
    private static StoredProcedureSnapshot CreateSnapshot(
        string procedureName = "GetUsers",
        string definitionHash = "ABC123")
    {
        return new StoredProcedureSnapshot
        {
            ObjectId = 1,
            SchemaName = "dbo",
            ProcedureName = procedureName,
            Definition = "SELECT 1;",
            DefinitionHash = definitionHash,
            CreatedAt = new DateTime(
                2026, 1, 1,
                10, 0, 0,
                DateTimeKind.Utc),
            ModifiedAt = new DateTime(
                2026, 1, 2,
                10, 0, 0,
                DateTimeKind.Utc),
            CapturedAtUtc = new DateTime(
                2026, 1, 3,
                10, 0, 0,
                DateTimeKind.Utc)
        };
    }
}