using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SqlHistory.Core.Models;
using SqlHistory.Core.Versioning;
using SqlHistory.PostgreSql.Routines;

namespace SqlHistory.PostgreSql.Tests;

/// <summary>
/// Runs only when SQLHISTORY_TEST_POSTGRES holds a connection string, e.g.
/// "Host=localhost;Database=sqlhistory_demo;Username=me". Otherwise the test is reported as skipped.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(PostgresTestSchema.ConnectionString))
            Skip = "Set SQLHISTORY_TEST_POSTGRES to a PostgreSQL connection string to run this test.";
    }
}

/// <summary>
/// A uniquely named schema that is dropped afterwards. Tests only create objects inside it.
/// </summary>
public sealed class PostgresTestSchema : IAsyncDisposable
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("SQLHISTORY_TEST_POSTGRES");

    private PostgresTestSchema(string name)
    {
        Name = name;
        ConnectionFactory = new PostgreSqlConnectionFactory(ConnectionString!);
        SnapshotFactory = new StoredProcedureSnapshotFactory(TimeProvider.System);
        Reader = new PostgreSqlRoutineReader(ConnectionFactory, NullLogger<PostgreSqlRoutineReader>.Instance);
        Restorer = new PostgreSqlRoutineRestorer(ConnectionFactory, SnapshotFactory);
    }

    public string Name { get; }

    public PostgreSqlConnectionFactory ConnectionFactory { get; }

    public StoredProcedureSnapshotFactory SnapshotFactory { get; }

    public PostgreSqlRoutineReader Reader { get; }

    public PostgreSqlRoutineRestorer Restorer { get; }

    public static async Task<PostgresTestSchema> CreateAsync()
    {
        var schema = new PostgresTestSchema("sqlhistory_test_" + Guid.NewGuid().ToString("N")[..12]);
        await schema.ExecuteAsync($"CREATE SCHEMA {schema.Name};");
        return schema;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var conn = await ConnectionFactory.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, conn);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        await using var conn = await ConnectionFactory.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, conn);
        return await command.ExecuteScalarAsync();
    }

    public async Task<IReadOnlyList<StoredProcedureDefinition>> ReadRoutinesAsync()
    {
        var all = await Reader.GetStoredProceduresAsync();
        return all.Where(x => x.SchemaName == Name).ToList();
    }

    /// <summary>Reads a routine as it is now and turns it into a stored version, as a scan would.</summary>
    public async Task<StoredProcedureVersion> CaptureVersionAsync(string name, string identityArguments,
        int versionNumber, RoutineType routineType = RoutineType.Function)
    {
        var identity = new StoredProcedureIdentity(SourceProviders.PostgreSql, Name, name, routineType, identityArguments);
        var definition = await Reader.GetRoutineAsync(identity)
                         ?? throw new InvalidOperationException($"Routine {name}({identityArguments}) not found.");

        return ToVersion(SnapshotFactory.Create(definition), versionNumber);
    }

    public static StoredProcedureVersion ToVersion(StoredProcedureSnapshot snapshot, int versionNumber)
    {
        return new StoredProcedureVersion
        {
            Id = versionNumber,
            VersionNumber = versionNumber,
            ObjectId = snapshot.ObjectId,
            Provider = snapshot.Provider,
            RoutineType = snapshot.RoutineType,
            IdentityArguments = snapshot.IdentityArguments,
            SchemaName = snapshot.SchemaName,
            ProcedureName = snapshot.ProcedureName,
            Definition = snapshot.Definition,
            DefinitionHash = snapshot.DefinitionHash,
            CreatedAt = snapshot.CreatedAt,
            ModifiedAt = snapshot.ModifiedAt,
            CapturedAtUtc = snapshot.CapturedAtUtc
        };
    }

    public async ValueTask DisposeAsync()
    {
        await ExecuteAsync($"DROP SCHEMA IF EXISTS {Name} CASCADE;");
        await ConnectionFactory.DisposeAsync();
    }
}
