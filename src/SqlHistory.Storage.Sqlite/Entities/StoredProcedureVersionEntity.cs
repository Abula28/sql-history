namespace SqlHistory.Storage.Sqlite.Entities;

internal sealed class StoredProcedureVersionEntity
{
    public long Id { get; set; }

    public int VersionNumber { get; set; }

    public int ObjectId { get; set; }

    public required string SchemaName { get; set; }

    public required string ProcedureName { get; set; }

    public string? Definition { get; set; }

    public required string DefinitionHash { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ModifiedAt { get; set; }

    public DateTime CapturedAtUtc { get; set; }
}