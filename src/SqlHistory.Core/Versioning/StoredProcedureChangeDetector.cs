using SqlHistory.Core.Models;

namespace SqlHistory.Core.Versioning;

public class StoredProcedureChangeDetector
{
    public ProcedureChangeStatus Detect(StoredProcedureSnapshot? prev, StoredProcedureSnapshot curr)
    {
        if (prev == null) return ProcedureChangeStatus.New;

        if (prev.DefinitionHash == curr.DefinitionHash) return ProcedureChangeStatus.Unchanged;
        
        return ProcedureChangeStatus.Modified;
    }
}