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
    public async Task AddVersionAsync_ShouldAddVersion_AndReturnMappedEntity()
    {
        await using var dbContext =
            CreateDbContext();

        var snapShot = CreateSnapshot();
        
        var repository = new StoredProcedureHistoryRepository(dbContext);
        var result = await repository.AddVersionAsync(snapShot);
        
        Assert.NotNull(result);
    }
    
    
    [Fact]
    public async Task AddVersionAsync_ShouldCreateVersionOne_WhenNoPreviousVersionsExist()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        var repository =
            new StoredProcedureHistoryRepository(dbContext);

        var snapshot = CreateSnapshot();

        // Act
        var result =
            await repository.AddVersionAsync(snapshot);

        // Assert
        Assert.Equal(1, result.VersionNumber);
    }
    
    [Fact]
    public async Task AddVersionAsync_ShouldIncrementVersionNumber_ForSameProcedure()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        var repository =
            new StoredProcedureHistoryRepository(dbContext);

        var first =
            CreateSnapshot(
                definitionHash: "HASH1");

        var second =
            CreateSnapshot(
                definitionHash: "HASH2");

        // Act
        var version1 =
            await repository.AddVersionAsync(first);

        var version2 =
            await repository.AddVersionAsync(second);

        // Assert
        Assert.Equal(1, version1.VersionNumber);
        Assert.Equal(2, version2.VersionNumber);
    }
    
    
    [Fact]
    public async Task AddVersionAsync_ShouldKeepIndependentVersionNumbers_PerProcedure()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        var repository =
            new StoredProcedureHistoryRepository(dbContext);

        var usersV1 =
            CreateSnapshot(
                procedureName: "GetUsers",
                definitionHash: "HASH1");

        var usersV2 =
            CreateSnapshot(
                procedureName: "GetUsers",
                definitionHash: "HASH2");

        var ordersV1 =
            CreateSnapshot(
                procedureName: "GetOrders",
                definitionHash: "HASH3");

        // Act
        var firstUsers =
            await repository.AddVersionAsync(usersV1);

        var secondUsers =
            await repository.AddVersionAsync(usersV2);

        var firstOrders =
            await repository.AddVersionAsync(ordersV1);

        // Assert
        Assert.Equal(1, firstUsers.VersionNumber);
        Assert.Equal(2, secondUsers.VersionNumber);
        Assert.Equal(1, firstOrders.VersionNumber);
    }
    
    [Fact]
    public async Task GetLatestVersionAsync_ShouldReturnHighestVersion()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        var repository =
            new StoredProcedureHistoryRepository(dbContext);

        await repository.AddVersionAsync(
            CreateSnapshot(definitionHash: "HASH1"));

        await repository.AddVersionAsync(
            CreateSnapshot(definitionHash: "HASH2"));

        await repository.AddVersionAsync(
            CreateSnapshot(definitionHash: "HASH3"));

        // Act
        var latest =
            await repository.GetLatestVersionAsync(
                "dbo",
                "GetUsers");

        // Assert
        Assert.NotNull(latest);

        Assert.Equal(3, latest.VersionNumber);
        Assert.Equal("HASH3", latest.DefinitionHash);
    }
    
    [Fact]
    public async Task AddVersionAsync_ShouldPreserveDefinitionHash()
    {
        // Arrange
        await using var dbContext = CreateDbContext();

        var repository =
            new StoredProcedureHistoryRepository(dbContext);

        var expectedHash =
            "A123456789012345678901234567890123456789012345678901234567890123";

        var snapshot =
            CreateSnapshot(
                definitionHash: expectedHash);

        // Act
        await repository.AddVersionAsync(snapshot);

        var loaded =
            await repository.GetLatestVersionAsync(
                snapshot.SchemaName,
                snapshot.ProcedureName);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(
            expectedHash,
            loaded.DefinitionHash);
    }
    
    
    [Fact]
    public async Task StoredVersion_ShouldPersistAcrossDbContextInstances()
    {
        // Arrange
        var snapshot = CreateSnapshot();

        await using (var firstContext = CreateDbContext())
        {
            var firstRepository =
                new StoredProcedureHistoryRepository(firstContext);

            await firstRepository.AddVersionAsync(snapshot);
        }

        // Act
        await using var secondContext = CreateDbContext();

        var secondRepository =
            new StoredProcedureHistoryRepository(secondContext);

        var loaded =
            await secondRepository.GetLatestVersionAsync(
                snapshot.SchemaName,
                snapshot.ProcedureName);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(1, loaded.VersionNumber);
        Assert.Equal(
            snapshot.DefinitionHash,
            loaded.DefinitionHash);
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