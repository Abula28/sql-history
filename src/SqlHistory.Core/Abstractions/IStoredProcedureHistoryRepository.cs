using SqlHistory.Core.Models;

namespace SqlHistory.Core.Abstractions;

public interface IStoredProcedureHistoryRepository
{
    /// <summary>
    /// Latest version of a SQL Server stored procedure. Kept for the CLI;
    /// use the <see cref="StoredProcedureIdentity"/> overload for other providers.
    /// </summary>
    Task<StoredProcedureVersion?> GetLatestVersionAsync(string schemaName, string procedureName,
        CancellationToken cancellationToken = default);

    Task<StoredProcedureVersion?> GetLatestVersionAsync(StoredProcedureIdentity identity,
        CancellationToken cancellationToken = default);

    /// <summary>All versions of one routine, newest first.</summary>
    Task<IReadOnlyList<StoredProcedureVersion>> GetVersionsAsync(StoredProcedureIdentity identity,
        CancellationToken cancellationToken = default);

    Task<StoredProcedureVersion?> GetVersionAsync(StoredProcedureIdentity identity, int versionNumber,
        CancellationToken cancellationToken = default);

    /// <summary>The latest version of every routine ever recorded for a provider.</summary>
    Task<IReadOnlyList<StoredProcedureVersion>> GetLatestVersionsAsync(string provider,
        CancellationToken cancellationToken = default);

    Task<StoredProcedureVersion> AddVersionAsync(StoredProcedureSnapshot snapshot,
        CancellationToken cancellationToken = default);
}