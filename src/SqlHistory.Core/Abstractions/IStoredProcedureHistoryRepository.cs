using SqlHistory.Core.Models;

namespace SqlHistory.Core.Abstractions;

public interface IStoredProcedureHistoryRepository
{
    Task<StoredProcedureVersion?> GetLatestVersionAsync(string schemaName, string procedureName,
        CancellationToken cancellationToken = default);

    Task<StoredProcedureVersion> AddVersionAsync(StoredProcedureSnapshot snapshot,
        CancellationToken cancellationToken = default);
}