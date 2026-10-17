namespace SqlHistory.Core.Models;

/// <summary>
/// Uniquely identifies a routine across providers and overloads.
/// IdentityArguments holds the argument types (e.g. "integer, date") and is empty
/// for providers without overloading such as SQL Server.
/// </summary>
public sealed record StoredProcedureIdentity(
    string Provider,
    string SchemaName,
    string ProcedureName,
    RoutineType RoutineType,
    string IdentityArguments);
