namespace SqlHistory.Core.Models;

public class StoredProcedureDefinition
{
    public required int ObjectId { get; init; }

    public required string SchemaName { get; init; }

    public required string ProcedureName { get; init; }

    public required string? Definition { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime ModifiedAt { get; init; }
}