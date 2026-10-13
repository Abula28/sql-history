namespace SqlHistory.Core.Sql;

public class SqlDefinitionNormalizer
{
    public static string Normalize(string? definition)
    {
        if (definition == null) return string.Empty;
        var normalized = definition
            .TrimStart('\uFEFF') // remove BOM
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .TrimEnd();
        
        return normalized;
    }
}