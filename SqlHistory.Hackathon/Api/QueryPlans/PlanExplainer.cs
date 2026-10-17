using Npgsql;
using SqlHistory.PostgreSql;

namespace SqlHistory.Hackathon.Api.QueryPlans;

public sealed record ExplainResult(string PlanJson, string Database, string ServerVersion);

/// <summary>
/// Runs EXPLAIN (estimated plan, never ANALYZE) for catalog queries only, inside a READ ONLY
/// transaction with a statement timeout that is always rolled back. Even if planning evaluates a
/// function, it cannot write anything.
/// </summary>
public sealed class PlanExplainer
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;

    public PlanExplainer(PostgreSqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ExplainResult> ExplainAsync(PlanQuery query, CancellationToken cancellationToken = default)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await conn.BeginTransactionAsync(cancellationToken);

        await using (var setup = new NpgsqlCommand(
                         "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '5s';", conn, transaction))
        {
            await setup.ExecuteNonQueryAsync(cancellationToken);
        }

        string planJson;
        await using (var explain = new NpgsqlCommand($"EXPLAIN (FORMAT JSON, COSTS TRUE) {query.Sql}", conn, transaction))
        {
            planJson = (string)(await explain.ExecuteScalarAsync(cancellationToken))!;
        }

        string database, version;
        await using (var info = new NpgsqlCommand(
                         "SELECT current_database(), current_setting('server_version');", conn, transaction))
        await using (var reader = await info.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            database = reader.GetString(0);
            version = reader.GetString(1);
        }

        await transaction.RollbackAsync(cancellationToken);
        return new ExplainResult(planJson, database, version);
    }
}
