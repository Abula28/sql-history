using System.Text.RegularExpressions;
using Npgsql;
using SqlHistory.Core.Models;
using SqlHistory.Core.Versioning;

namespace SqlHistory.PostgreSql.Routines;

public enum RestoreStatus
{
    Succeeded,
    NotSupported,
    RoutineNotFound,
    AlreadyCurrent,
    Conflict
}

public sealed record RestoreResult(
    RestoreStatus Status,
    string Message,
    string? CurrentDefinition = null,
    string? CurrentDefinitionHash = null);

/// <summary>
/// Restores a stored historical definition of a PostgreSQL routine by executing it
/// inside a transaction and verifying the outcome before committing.
///
/// Only definitions recorded in history are executed, never caller-supplied SQL.
/// Only in-place replacements are supported: the routine must still exist with the same
/// identity, and PostgreSQL must accept the definition as CREATE OR REPLACE. Routines are
/// never dropped, so changes of return type or parameter names are rejected by PostgreSQL
/// and reported back as not supported.
/// </summary>
public sealed partial class PostgreSqlRoutineRestorer
{
    private const string NeverDropsNote =
        "SQL Time Machine only restores versions PostgreSQL can apply with CREATE OR REPLACE; it never drops routines.";

    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly StoredProcedureSnapshotFactory _snapshotFactory;

    public PostgreSqlRoutineRestorer(PostgreSqlConnectionFactory connectionFactory,
        StoredProcedureSnapshotFactory snapshotFactory)
    {
        _connectionFactory = connectionFactory;
        _snapshotFactory = snapshotFactory;
    }

    /// <summary>Runs the full restore, then rolls it back. Nothing is changed.</summary>
    public Task<RestoreResult> ValidateAsync(StoredProcedureVersion target,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(target, expectedCurrentHash: null, commit: false, cancellationToken);
    }

    /// <param name="expectedCurrentHash">
    /// Hash of the definition the user reviewed. The restore is refused if the routine changed since.
    /// </param>
    public Task<RestoreResult> RestoreAsync(StoredProcedureVersion target, string expectedCurrentHash,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(target, expectedCurrentHash, commit: true, cancellationToken);
    }

    /// <summary>Checks that need no database. Returns null when the version may be attempted.</summary>
    public static string? GetUnsupportedReason(StoredProcedureVersion target)
    {
        if (target.Provider != SourceProviders.PostgreSql)
            return $"Restore is only implemented for PostgreSQL, not {target.Provider}.";

        if (string.IsNullOrWhiteSpace(target.Definition))
            return "This version has no stored definition.";

        var match = CreateOrReplaceRegex().Match(target.Definition);
        if (!match.Success)
            return "The stored definition is not a CREATE OR REPLACE FUNCTION/PROCEDURE statement.";

        var kind = match.Groups["kind"].Value.ToUpperInvariant();
        var expectedKind = target.RoutineType == RoutineType.Function ? "FUNCTION" : "PROCEDURE";
        if (kind != expectedKind)
            return $"The stored definition creates a {kind.ToLowerInvariant()}, but this routine is a {expectedKind.ToLowerInvariant()}.";

        return null;
    }

    private async Task<RestoreResult> ExecuteAsync(StoredProcedureVersion target, string? expectedCurrentHash,
        bool commit, CancellationToken cancellationToken)
    {
        var unsupportedReason = GetUnsupportedReason(target);
        if (unsupportedReason != null)
            return new RestoreResult(RestoreStatus.NotSupported, unsupportedReason);

        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        // Every return before CommitAsync rolls back when the transaction is disposed.
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        await ExecuteNonQueryAsync(conn, transaction,
            "SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '30s';", cancellationToken);

        var current = await ReadRoutineAsync(conn, transaction, target.Identity, cancellationToken);
        if (current == null)
        {
            return new RestoreResult(RestoreStatus.RoutineNotFound,
                "The routine no longer exists in the database. Restoring dropped routines is not supported.");
        }

        var currentHash = Hash(current);

        if (expectedCurrentHash != null && currentHash != expectedCurrentHash)
        {
            return new RestoreResult(RestoreStatus.Conflict,
                "The routine changed after the restore was previewed. Preview it again before restoring.",
                current.Definition, currentHash);
        }

        if (currentHash == target.DefinitionHash)
        {
            return new RestoreResult(RestoreStatus.AlreadyCurrent,
                $"The database already matches version {target.VersionNumber}.",
                current.Definition, currentHash);
        }

        // pg_get_functiondef quotes names exactly like format('%I'), so asking the server
        // gives the header a definition for this routine must start with.
        var expectedHeader = await GetExpectedHeaderAsync(conn, transaction, target, cancellationToken);
        if (!target.Definition!.TrimStart().StartsWith(expectedHeader, StringComparison.Ordinal))
        {
            return new RestoreResult(RestoreStatus.NotSupported,
                $"The stored definition does not start with \"{expectedHeader}\", so it may not target this routine.",
                current.Definition, currentHash);
        }

        var routineCountBefore = await CountRoutinesAsync(conn, transaction, cancellationToken);

        try
        {
            // A batch command is sent as one extended-protocol statement, which PostgreSQL
            // refuses if it contains more than one command.
            await using var batch = new NpgsqlBatch(conn, transaction);
            batch.BatchCommands.Add(new NpgsqlBatchCommand(target.Definition));
            await batch.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex)
        {
            return new RestoreResult(RestoreStatus.NotSupported,
                $"PostgreSQL rejected the definition: {ex.MessageText}. {NeverDropsNote}",
                current.Definition, currentHash);
        }

        var restored = await ReadRoutineAsync(conn, transaction, target.Identity, cancellationToken);
        var routineCountAfter = await CountRoutinesAsync(conn, transaction, cancellationToken);

        var verificationError =
            restored == null ? "the routine is missing after executing the definition"
            : restored.ObjectId != current.ObjectId ? "the routine was recreated instead of replaced"
            : routineCountAfter != routineCountBefore ? "the number of routines in the database changed"
            : Hash(restored) != target.DefinitionHash ? "the resulting definition differs from the stored version"
            : null;

        if (verificationError != null)
        {
            return new RestoreResult(RestoreStatus.NotSupported,
                $"Verification failed: {verificationError}. Nothing was changed.",
                current.Definition, currentHash);
        }

        if (!commit)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new RestoreResult(RestoreStatus.Succeeded,
                $"Dry run succeeded: version {target.VersionNumber} can be restored. Nothing was changed.",
                current.Definition, currentHash);
        }

        await transaction.CommitAsync(cancellationToken);
        return new RestoreResult(RestoreStatus.Succeeded,
            $"Restored version {target.VersionNumber} and verified the resulting definition.",
            restored!.Definition, target.DefinitionHash);
    }

    private string Hash(StoredProcedureDefinition definition)
    {
        return _snapshotFactory.Create(definition).DefinitionHash;
    }

    private static async Task<StoredProcedureDefinition?> ReadRoutineAsync(NpgsqlConnection conn,
        NpgsqlTransaction transaction, StoredProcedureIdentity identity, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PostgreSqlRoutineCatalog.SelectByIdentity, conn, transaction);
        PostgreSqlRoutineCatalog.AddIdentityParameters(command, identity);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? PostgreSqlRoutineCatalog.Read(reader) : null;
    }

    private static async Task<string> GetExpectedHeaderAsync(NpgsqlConnection conn, NpgsqlTransaction transaction,
        StoredProcedureVersion target, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT format('%I.%I(', $1, $2);", conn, transaction);
        command.Parameters.Add(new NpgsqlParameter { Value = target.SchemaName });
        command.Parameters.Add(new NpgsqlParameter { Value = target.ProcedureName });

        var qualifiedName = (string)(await command.ExecuteScalarAsync(cancellationToken))!;
        var kind = target.RoutineType == RoutineType.Function ? "FUNCTION" : "PROCEDURE";

        return $"CREATE OR REPLACE {kind} {qualifiedName}";
    }

    private static async Task<long> CountRoutinesAsync(NpgsqlConnection conn, NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_catalog.pg_proc;", conn, transaction);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task ExecuteNonQueryAsync(NpgsqlConnection conn, NpgsqlTransaction transaction, string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, conn, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    [GeneratedRegex(@"^\s*CREATE\s+OR\s+REPLACE\s+(?<kind>FUNCTION|PROCEDURE)\s", RegexOptions.IgnoreCase)]
    private static partial Regex CreateOrReplaceRegex();
}
