namespace SqlHistory.Core.Models;

public class StoredProcedureDefinition
{
    /// <summary>
    /// Provider-specific object id (SQL Server object_id, PostgreSQL oid). Informational only:
    /// it changes when a routine is dropped and recreated, so it is not part of the identity.
    /// </summary>
    public required long ObjectId { get; init; }

    public string Provider { get; init; } = SourceProviders.SqlServer;

    public RoutineType RoutineType { get; init; } = RoutineType.Procedure;

    public string IdentityArguments { get; init; } = string.Empty;

    public required string SchemaName { get; init; }

    public required string ProcedureName { get; init; }

    public required string? Definition { get; init; }

    /// <summary>Null when the provider does not track it (PostgreSQL).</summary>
    public required DateTime? CreatedAt { get; init; }

    /// <summary>Null when the provider does not track it (PostgreSQL).</summary>
    public required DateTime? ModifiedAt { get; init; }

    public StoredProcedureIdentity Identity =>
        new(Provider, SchemaName, ProcedureName, RoutineType, IdentityArguments);
}
