using SqlHistory.Core.Models;

namespace SqlHistory.Core.Abstractions;

public interface IStoredProcedureReader
{
    Task<IReadOnlyCollection<StoredProcedureDefinition>> GetStoredProceduresAsync( CancellationToken cancellationToken = default);
}