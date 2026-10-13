using System.Security.Cryptography;
using System.Text;
using SqlHistory.Core.Models;
using SqlHistory.Core.Sql;

namespace SqlHistory.Core.Versioning;

public class StoredProcedureSnapshotFactory
{
    private readonly TimeProvider _timeProvider;
    public StoredProcedureSnapshotFactory(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public StoredProcedureSnapshot Create(StoredProcedureDefinition definition)
    {
        var normalized = SqlDefinitionNormalizer.Normalize(definition.Definition);
        var hash = CalculateHash(normalized);

        var result = new StoredProcedureSnapshot
        {
            ObjectId = definition.ObjectId,
            SchemaName = definition.SchemaName,
            ProcedureName = definition.ProcedureName,
            Definition = definition.Definition,
            DefinitionHash = hash,
            CreatedAt = definition.CreatedAt,
            ModifiedAt = definition.ModifiedAt,
            CapturedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };

        return result;
    }

    private string CalculateHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hasBytes = SHA256.HashData(bytes);
        
        var hash = Convert.ToHexString(hasBytes);
        return hash;
    }
}