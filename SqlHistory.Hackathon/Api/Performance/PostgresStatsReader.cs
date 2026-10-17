using Npgsql;
using SqlHistory.PostgreSql;

namespace SqlHistory.Hackathon.Api.Performance;

public sealed record StatsAvailability(
    bool StatementsPreloaded,
    bool StatementsExtensionInstalled,
    string? StatementsSchema,
    string TrackFunctions,
    string? StatementsSetupHint,
    string? FunctionsSetupHint)
{
    public bool StatementsAvailable => StatementsPreloaded && StatementsExtensionInstalled;

    /// <summary>"pl" only tracks procedural-language functions; "all" adds SQL and C functions.</summary>
    public bool FunctionsTracked => TrackFunctions is "pl" or "all";
}

public sealed record StatementKey(long QueryId, long UserId, bool TopLevel);

/// <summary>A row of pg_stat_statements for the current database. Times are milliseconds.</summary>
public sealed record StatementStat(
    StatementKey Key,
    string Role,
    string Query,
    long Calls,
    double TotalExecTimeMs,
    double MinExecTimeMs,
    double MaxExecTimeMs,
    double MeanExecTimeMs,
    long Rows);

public sealed record FunctionKey(string SchemaName, string FunctionName, string IdentityArguments);

/// <summary>A row of pg_stat_user_functions. Total includes nested calls; self excludes them.</summary>
public sealed record FunctionStat(
    FunctionKey Key,
    long FunctionOid,
    char Kind,
    long Calls,
    double TotalTimeMs,
    double SelfTimeMs);

public sealed record StatsReading(
    DateTime ReadAtUtc,
    StatsAvailability Availability,
    DateTime? StatementsResetAtUtc,
    long? StatementsDealloc,
    DateTime? DatabaseStatsResetAtUtc,
    IReadOnlyList<StatementStat> Statements,
    IReadOnlyList<FunctionStat> Functions);

/// <summary>
/// Reads cumulative statistics. Only reads: it never creates extensions or changes settings.
/// </summary>
public sealed class PostgresStatsReader
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public PostgresStatsReader(PostgreSqlConnectionFactory connectionFactory, TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task<StatsAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        return await GetAvailabilityAsync(conn, cancellationToken);
    }

    public async Task<StatsReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var availability = await GetAvailabilityAsync(conn, cancellationToken);
        var readAt = _timeProvider.GetUtcNow().UtcDateTime;

        DateTime? statementsReset = null;
        long? dealloc = null;
        var statements = new List<StatementStat>();

        if (availability.StatementsAvailable)
        {
            var schema = QuoteIdentifier(availability.StatementsSchema!);

            await using (var command = new NpgsqlCommand(
                             $"SELECT dealloc, stats_reset FROM {schema}.pg_stat_statements_info;", conn))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (await reader.ReadAsync(cancellationToken))
                {
                    dealloc = reader.GetInt64(0);
                    statementsReset = reader.IsDBNull(1) ? null : reader.GetDateTime(1).ToUniversalTime();
                }
            }

            await using (var command = new NpgsqlCommand($"""
                             SELECT s.queryid, s.userid::bigint, s.toplevel, coalesce(r.rolname, s.userid::text),
                                    s.query, s.calls, s.total_exec_time, s.min_exec_time, s.max_exec_time,
                                    s.mean_exec_time, s.rows
                             FROM {schema}.pg_stat_statements AS s
                             LEFT JOIN pg_catalog.pg_roles AS r ON r.oid = s.userid
                             WHERE s.dbid = (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database())
                               AND s.queryid IS NOT NULL;
                             """, conn))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    statements.Add(new StatementStat(
                        new StatementKey(reader.GetInt64(0), reader.GetInt64(1), reader.GetBoolean(2)),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? "<query text unavailable>" : reader.GetString(4),
                        reader.GetInt64(5),
                        reader.GetDouble(6),
                        reader.GetDouble(7),
                        reader.GetDouble(8),
                        reader.GetDouble(9),
                        reader.GetInt64(10)));
                }
            }
        }

        DateTime? databaseReset;
        await using (var command = new NpgsqlCommand(
                         "SELECT stats_reset FROM pg_catalog.pg_stat_database WHERE datname = current_database();", conn))
        {
            var value = await command.ExecuteScalarAsync(cancellationToken);
            databaseReset = value is DateTime reset ? reset.ToUniversalTime() : null;
        }

        var functions = new List<FunctionStat>();
        await using (var command = new NpgsqlCommand("""
                         SELECT f.funcid::bigint, f.schemaname, f.funcname,
                                pg_catalog.oidvectortypes(p.proargtypes), p.prokind,
                                f.calls, f.total_time, f.self_time
                         FROM pg_catalog.pg_stat_user_functions AS f
                         INNER JOIN pg_catalog.pg_proc AS p ON p.oid = f.funcid;
                         """, conn))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                functions.Add(new FunctionStat(
                    new FunctionKey(reader.GetString(1), reader.GetString(2), reader.GetString(3)),
                    reader.GetInt64(0),
                    reader.GetChar(4),
                    reader.GetInt64(5),
                    reader.GetDouble(6),
                    reader.GetDouble(7)));
            }
        }

        return new StatsReading(readAt, availability, statementsReset, dealloc, databaseReset, statements, functions);
    }

    private static async Task<StatsAvailability> GetAvailabilityAsync(NpgsqlConnection conn,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT 'pg_stat_statements' = ANY (string_to_array(replace(current_setting('shared_preload_libraries'), ' ', ''), ',')),
                   (SELECT n.nspname FROM pg_catalog.pg_extension AS e
                    INNER JOIN pg_catalog.pg_namespace AS n ON n.oid = e.extnamespace
                    WHERE e.extname = 'pg_stat_statements'),
                   current_setting('track_functions');
            """, conn);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        var preloaded = reader.GetBoolean(0);
        var schema = reader.IsDBNull(1) ? null : reader.GetString(1);
        var trackFunctions = reader.GetString(2);

        var statementsHint =
            !preloaded
                ? "pg_stat_statements is not loaded. As a superuser run ALTER SYSTEM SET shared_preload_libraries = 'pg_stat_statements'; then restart PostgreSQL (sudo systemctl restart postgresql), then CREATE EXTENSION pg_stat_statements; in this database."
                : schema == null
                    ? "pg_stat_statements is loaded but not created in this database. Run CREATE EXTENSION pg_stat_statements; in this database."
                    : null;

        var functionsHint = trackFunctions is "pl" or "all"
            ? null
            : "Function statistics are off (track_functions = none). As a superuser run ALTER DATABASE <database> SET track_functions = 'all'; it applies to new connections.";

        return new StatsAvailability(preloaded, schema != null, schema, trackFunctions, statementsHint, functionsHint);
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
