using Microsoft.Extensions.Logging;
using Npgsql;
using SqlHistory.Core.Abstractions;
using SqlHistory.Core.Models;

namespace SqlHistory.PostgreSql.Routines;

/// <summary>
/// Reads PostgreSQL functions and procedures. Both are returned as
/// <see cref="StoredProcedureDefinition"/> with <see cref="RoutineType"/> set accordingly.
/// </summary>
public sealed class PostgreSqlRoutineReader : IStoredProcedureReader
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly ILogger<PostgreSqlRoutineReader> _logger;

    public PostgreSqlRoutineReader(
        PostgreSqlConnectionFactory connectionFactory,
        ILogger<PostgreSqlRoutineReader> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<StoredProcedureDefinition>> GetStoredProceduresAsync(
        CancellationToken cancellationToken = default)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        var routines = new List<StoredProcedureDefinition>();

        await using (var command = new NpgsqlCommand(PostgreSqlRoutineCatalog.SelectAll, conn))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                routines.Add(PostgreSqlRoutineCatalog.Read(reader));
            }
        }

        await using (var command = new NpgsqlCommand(PostgreSqlRoutineCatalog.CountUnsupported, conn))
        {
            var unsupported = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
            if (unsupported > 0)
            {
                _logger.LogInformation(
                    "Skipped {Count} aggregate or window functions; they are not tracked.", unsupported);
            }
        }

        _logger.LogDebug("Found {Count} PostgreSQL routines.", routines.Count);

        return routines;
    }

    public async Task<StoredProcedureDefinition?> GetRoutineAsync(StoredProcedureIdentity identity,
        CancellationToken cancellationToken = default)
    {
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(PostgreSqlRoutineCatalog.SelectByIdentity, conn);
        PostgreSqlRoutineCatalog.AddIdentityParameters(command, identity);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? PostgreSqlRoutineCatalog.Read(reader) : null;
    }
}
