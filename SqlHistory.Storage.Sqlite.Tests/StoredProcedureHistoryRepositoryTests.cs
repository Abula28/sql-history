using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SqlHistory.Core.Models;

namespace SqlHistory.Storage.Sqlite.Tests;

public class StoredProcedureHistoryRepositoryTests
{
    [Fact]
    public async Task Migration_ShouldKeepExistingRows_AsSqlServerProcedures()
    {
        // Arrange: a history database as the CLI created it before PostgreSQL support.
        await using var database = await SqliteTestDatabase.CreateAsync(migrate: false);
        await using (var context = database.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync("20261013125715_InitialHistoryStorage");
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO ProcedureVersions
                    (VersionNumber, ObjectId, SchemaName, ProcedureName, Definition, DefinitionHash,
                     CreatedAt, ModifiedAt, CapturedAtUtc)
                VALUES
                    (1, 42, 'dbo', 'GetUsers', 'SELECT 1;', 'HASH1',
                     '2026-01-01 00:00:00', '2026-01-02 00:00:00', '2026-01-03 00:00:00');
                """);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        // Assert
        var version = await database.CreateRepository().GetLatestVersionAsync("dbo", "GetUsers");

        Assert.NotNull(version);
        Assert.Equal(SourceProviders.SqlServer, version.Provider);
        Assert.Equal(RoutineType.Procedure, version.RoutineType);
        Assert.Equal(string.Empty, version.IdentityArguments);
        Assert.Equal(42, version.ObjectId);
        Assert.Equal("SELECT 1;", version.Definition);
        Assert.Equal(new DateTime(2026, 1, 1), version.CreatedAt);
    }

    [Fact]
    public async Task AddVersion_ShouldNumberOverloadsIndependently()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var repository = database.CreateRepository();

        var byInteger = await repository.AddVersionAsync(PgFunction("total", "integer", "v1"));
        var byText = await repository.AddVersionAsync(PgFunction("total", "text", "v1"));
        var byIntegerAgain = await repository.AddVersionAsync(PgFunction("total", "integer", "v2"));

        Assert.Equal(1, byInteger.VersionNumber);
        Assert.Equal(1, byText.VersionNumber);
        Assert.Equal(2, byIntegerAgain.VersionNumber);
    }

    [Fact]
    public async Task Providers_ShouldNotShareHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var repository = database.CreateRepository();

        await repository.AddVersionAsync(Snapshot(SourceProviders.SqlServer, RoutineType.Procedure, "dbo", "Sync", "", "sql server"));
        await repository.AddVersionAsync(Snapshot(SourceProviders.PostgreSql, RoutineType.Procedure, "dbo", "Sync", "", "postgres"));

        var legacyLookup = await repository.GetLatestVersionAsync("dbo", "Sync");
        var postgres = await repository.GetLatestVersionAsync(
            new StoredProcedureIdentity(SourceProviders.PostgreSql, "dbo", "Sync", RoutineType.Procedure, ""));

        Assert.Equal("sql server", legacyLookup!.Definition);
        Assert.Equal(1, legacyLookup.VersionNumber);
        Assert.Equal("postgres", postgres!.Definition);
        Assert.Equal(1, postgres.VersionNumber);
    }

    [Fact]
    public async Task FunctionsAndProcedures_ShouldNotShareHistory()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var repository = database.CreateRepository();

        await repository.AddVersionAsync(Snapshot(SourceProviders.PostgreSql, RoutineType.Function, "public", "refresh", "", "function"));
        var procedure = await repository.AddVersionAsync(
            Snapshot(SourceProviders.PostgreSql, RoutineType.Procedure, "public", "refresh", "", "procedure"));

        Assert.Equal(1, procedure.VersionNumber);
    }

    [Fact]
    public async Task GetVersions_ShouldReturnNewestFirst_WithOriginalContent()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var repository = database.CreateRepository();
        var first = PgFunction("total", "integer", "SELECT 1;");

        await repository.AddVersionAsync(first);
        await repository.AddVersionAsync(PgFunction("total", "integer", "SELECT 2;"));
        await repository.AddVersionAsync(PgFunction("total", "integer", "SELECT 3;"));

        var versions = await repository.GetVersionsAsync(first.Identity);
        var historical = await repository.GetVersionAsync(first.Identity, 1);
        var missing = await repository.GetVersionAsync(first.Identity, 99);

        Assert.Equal([3, 2, 1], versions.Select(x => x.VersionNumber));
        Assert.Equal("SELECT 1;", historical!.Definition);
        Assert.Equal(first.DefinitionHash, historical.DefinitionHash);
        Assert.Null(historical.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, historical.CapturedAtUtc.Kind);
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetLatestVersions_ShouldReturnOneRowPerRoutine_ForProvider()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var repository = database.CreateRepository();

        await repository.AddVersionAsync(PgFunction("a", "", "a1"));
        await repository.AddVersionAsync(PgFunction("a", "", "a2"));
        await repository.AddVersionAsync(PgFunction("b", "integer", "b1"));
        await repository.AddVersionAsync(Snapshot(SourceProviders.SqlServer, RoutineType.Procedure, "dbo", "c", "", "c1"));

        var latest = await repository.GetLatestVersionsAsync(SourceProviders.PostgreSql);

        Assert.Equal(["a2", "b1"], latest.Select(x => x.Definition));
    }

    private static StoredProcedureSnapshot PgFunction(string name, string arguments, string definition)
    {
        return Snapshot(SourceProviders.PostgreSql, RoutineType.Function, "public", name, arguments, definition);
    }

    private static StoredProcedureSnapshot Snapshot(string provider, RoutineType routineType, string schema,
        string name, string arguments, string definition)
    {
        return new StoredProcedureSnapshot
        {
            ObjectId = 1,
            Provider = provider,
            RoutineType = routineType,
            IdentityArguments = arguments,
            SchemaName = schema,
            ProcedureName = name,
            Definition = definition,
            DefinitionHash = "HASH-" + definition,
            CreatedAt = null,
            ModifiedAt = null,
            CapturedAtUtc = DateTime.UtcNow
        };
    }
}
