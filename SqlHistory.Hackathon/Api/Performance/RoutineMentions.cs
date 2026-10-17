using System.Text.RegularExpressions;
using SqlHistory.Core.Models;

namespace SqlHistory.Hackathon.Api.Performance;

/// <summary>
/// Finds routines whose schema-qualified name appears as a call in a statement's text.
/// This is a text match, not attribution: pg_stat_statements counts statements, and a
/// statement that calls a routine also includes the caller's own work. Unqualified calls
/// (relying on search_path) are not matched, so a missing match proves nothing.
/// </summary>
public static class RoutineMentions
{
    public static IReadOnlyList<StoredProcedureIdentity> Find(string query,
        IEnumerable<StoredProcedureIdentity> routines)
    {
        return routines.Where(r => Pattern(r).IsMatch(query)).ToList();
    }

    public static Regex Pattern(StoredProcedureIdentity routine)
    {
        var schema = Identifier(routine.SchemaName);
        var name = Identifier(routine.ProcedureName);
        return new Regex($@"(?<![\w$""]){schema}\s*\.\s*{name}\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // Matches the identifier bare or double-quoted.
    private static string Identifier(string identifier)
    {
        var bare = Regex.Escape(identifier);
        var quoted = Regex.Escape("\"" + identifier.Replace("\"", "\"\"") + "\"");
        return $"(?:{quoted}|{bare}(?![\\w$]))";
    }
}
