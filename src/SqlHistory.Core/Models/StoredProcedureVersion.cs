namespace SqlHistory.Core.Models;

public sealed record StoredProcedureVersion
{
    public required long Id { get; init; }

    public required int VersionNumber { get; init; }

    public required long ObjectId { get; init; }

    public string Provider { get; init; } = SourceProviders.SqlServer;

    public RoutineType RoutineType { get; init; } = RoutineType.Procedure;

    public string IdentityArguments { get; init; } = string.Empty;

    public required string SchemaName { get; init; }

    public required string ProcedureName { get; init; }

    public required string? Definition { get; init; }

    public required string DefinitionHash { get; init; }

    public required DateTime? CreatedAt { get; init; }

    public required DateTime? ModifiedAt { get; init; }

    public required DateTime CapturedAtUtc { get; init; }

    public StoredProcedureIdentity Identity =>
        new(Provider, SchemaName, ProcedureName, RoutineType, IdentityArguments);
}
