using SqlHistory.Core.Abstractions;
using SqlHistory.Core.Models;
using SqlHistory.Core.Services;
using SqlHistory.Core.Versioning;

namespace SqlHistory.Storage.Sqlite.Tests;

/// <summary>
/// Scan service against real SQLite history; only the source database is replaced
/// by an in-memory list of definitions.
/// </summary>
public class SnapshotScanServiceTests
{
    [Fact]
    public async Task FirstScan_ShouldRecordVersionOne_ForEveryRoutine()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var source = new InMemoryReader(Function("active_users", "", "SELECT 1"), Function("total", "integer", "SELECT 2"));

        var result = await CreateService(database, source).ScanAsync();

        Assert.Equal(2, result.NewCount);
        Assert.All(result.Procedures, x => Assert.Equal(1, x.LatestVersion.VersionNumber));
    }

    [Fact]
    public async Task RepeatedScan_ShouldNotCreateDuplicateVersions_WhenNothingChanged()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var source = new InMemoryReader(Function("active_users", "", "SELECT 1"));

        await CreateService(database, source).ScanAsync();
        var second = await CreateService(database, source).ScanAsync();

        var versions = await database.CreateRepository().GetVersionsAsync(source.Routines[0].Identity);
        Assert.Equal(1, second.UnchangedCount);
        Assert.Single(versions);
    }

    [Fact]
    public async Task Scan_ShouldIgnoreLineEndingOnlyDifferences()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var source = new InMemoryReader(Function("active_users", "", "SELECT 1;\nSELECT 2;"));
        await CreateService(database, source).ScanAsync();

        source.Routines[0] = Function("active_users", "", "SELECT 1;\r\nSELECT 2;\n");
        var result = await CreateService(database, source).ScanAsync();

        Assert.Equal(1, result.UnchangedCount);
    }

    [Fact]
    public async Task Scan_ShouldCreateNextVersion_WhenDefinitionChanges()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var source = new InMemoryReader(Function("active_users", "", "WHERE is_active"));
        await CreateService(database, source).ScanAsync();

        source.Routines[0] = Function("active_users", "", "no filter");
        var result = await CreateService(database, source).ScanAsync();

        var scanned = Assert.Single(result.Procedures);
        Assert.Equal(ProcedureChangeStatus.Modified, scanned.Status);
        Assert.Equal(2, scanned.LatestVersion.VersionNumber);
        Assert.Equal("no filter", scanned.LatestVersion.Definition);
    }

    [Fact]
    public async Task Scan_ShouldRecordRevertAsNewVersion()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var source = new InMemoryReader(Function("active_users", "", "A"));

        await CreateService(database, source).ScanAsync();
        source.Routines[0] = Function("active_users", "", "B");
        await CreateService(database, source).ScanAsync();
        source.Routines[0] = Function("active_users", "", "A");
        var result = await CreateService(database, source).ScanAsync();

        var versions = await database.CreateRepository().GetVersionsAsync(source.Routines[0].Identity);
        Assert.Equal(ProcedureChangeStatus.Modified, result.Procedures.Single().Status);
        Assert.Equal(["A", "B", "A"], versions.Reverse().Select(x => x.Definition));
        Assert.Equal(versions[0].DefinitionHash, versions[2].DefinitionHash);
    }

    [Fact]
    public async Task Scan_ShouldKeepOverloadsSeparate()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var source = new InMemoryReader(Function("total", "integer", "int"), Function("total", "numeric", "numeric"));
        await CreateService(database, source).ScanAsync();

        source.Routines[1] = Function("total", "numeric", "numeric v2");
        var result = await CreateService(database, source).ScanAsync();

        Assert.Equal(1, result.UnchangedCount);
        Assert.Equal(1, result.ModifiedCount);
        Assert.Equal("numeric", result.Procedures.Single(x => x.Status == ProcedureChangeStatus.Modified)
            .Identity.IdentityArguments);
    }

    private static SnapshotScanService CreateService(SqliteTestDatabase database, IStoredProcedureReader reader)
    {
        return new SnapshotScanService(
            reader,
            database.CreateRepository(),
            new StoredProcedureSnapshotFactory(TimeProvider.System),
            new StoredProcedureChangeDetector(),
            TimeProvider.System);
    }

    private static StoredProcedureDefinition Function(string name, string arguments, string definition)
    {
        return new StoredProcedureDefinition
        {
            ObjectId = 1,
            Provider = SourceProviders.PostgreSql,
            RoutineType = RoutineType.Function,
            IdentityArguments = arguments,
            SchemaName = "public",
            ProcedureName = name,
            Definition = definition,
            CreatedAt = null,
            ModifiedAt = null
        };
    }

    private sealed class InMemoryReader : IStoredProcedureReader
    {
        public InMemoryReader(params StoredProcedureDefinition[] routines)
        {
            Routines = routines.ToList();
        }

        public List<StoredProcedureDefinition> Routines { get; }

        public Task<IReadOnlyCollection<StoredProcedureDefinition>> GetStoredProceduresAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<StoredProcedureDefinition>>(Routines.ToList());
        }
    }
}
