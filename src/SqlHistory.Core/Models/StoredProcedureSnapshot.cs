namespace SqlHistory.Core.Models;

public sealed record StoredProcedureSnapshot
{
    public required int ObjectId { get; init; }

    public required string SchemaName { get; init; }

    public required string ProcedureName { get; init; }

    public required string? Definition { get; init; }

    public required string DefinitionHash { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime ModifiedAt { get; init; }

    public required DateTime CapturedAtUtc { get; init; }
}