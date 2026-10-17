using Npgsql;

namespace SqlHistory.PostgreSql;

/// <summary>
/// Wraps a single <see cref="NpgsqlDataSource"/> so connections share one pool.
/// Register as a singleton.
/// </summary>
public sealed class PostgreSqlConnectionFactory : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgreSqlConnectionFactory(string connectionString)
    {
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    /// <summary>Database name from the connection string, safe to show in a UI.</summary>
    public string? DatabaseName =>
        new NpgsqlConnectionStringBuilder(_dataSource.ConnectionString).Database;

    public ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        return _dataSource.OpenConnectionAsync(cancellationToken);
    }

    /// <summary>Opens a connection and reports what it connected to. Throws if it cannot connect.</summary>
    public async Task<PostgreSqlServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT current_database(), current_user, current_setting('server_version');", conn);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return new PostgreSqlServerInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    public ValueTask DisposeAsync()
    {
        return _dataSource.DisposeAsync();
    }
}
