using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlHistory.Core.Abstractions;
using SqlHistory.Core.Models;
using SqlHistory.SqlServer;

public sealed class SqlServerStoreProceduresReader
    : IStoredProcedureReader
{
    private readonly SqlServerConnectionFactory _connectionFactory;
    private readonly ILogger<SqlServerStoreProceduresReader> _logger;

    public SqlServerStoreProceduresReader(
        SqlServerConnectionFactory connectionFactory,
        ILogger<SqlServerStoreProceduresReader> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<StoredProcedureDefinition>>
        GetStoredProceduresAsync(
            CancellationToken cancellationToken = default)
    {
        await using var conn =
            _connectionFactory.CreateConnection();

        await conn.OpenAsync(cancellationToken);

        _logger.LogInformation(
            "Connected to SQL Server.");

        await using var command = new SqlCommand("""
            SELECT 
                p.object_id, 
                s.name AS SchemaName, 
                p.name AS ProcedureName, 
                p.create_date, 
                p.modify_date, 
                m.definition 
            FROM sys.procedures AS p 
            INNER JOIN sys.schemas AS s 
                ON p.schema_id = s.schema_id 
            INNER JOIN sys.sql_modules AS m 
                ON p.object_id = m.object_id 
            WHERE p.is_ms_shipped = 0 
            ORDER BY s.name, p.name;
            """, conn);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var storedProcedures =
            new List<StoredProcedureDefinition>();

        while (await reader.ReadAsync(cancellationToken))
        {
            storedProcedures.Add(
                new StoredProcedureDefinition
                {
                    ObjectId =
                        reader.GetInt32(
                            reader.GetOrdinal("object_id")),

                    ProcedureName =
                        reader.GetString(
                            reader.GetOrdinal("ProcedureName")),

                    SchemaName =
                        reader.GetString(
                            reader.GetOrdinal("SchemaName")),

                    CreatedAt =
                        reader.GetDateTime(
                            reader.GetOrdinal("create_date")),

                    ModifiedAt =
                        reader.GetDateTime(
                            reader.GetOrdinal("modify_date")),

                    Definition =
                        reader.IsDBNull(
                            reader.GetOrdinal("definition"))
                            ? null
                            : reader.GetString(
                                reader.GetOrdinal("definition"))
                });
        }

        _logger.LogInformation(
            "Found {Count} stored procedures.",
            storedProcedures.Count);

        return storedProcedures;
    }
}