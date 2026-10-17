namespace SqlHistory.Core.Models;

/// <summary>
/// Names of the source databases a routine can be read from.
/// Stored with every version so routines from different providers never share history.
/// </summary>
public static class SourceProviders
{
    public const string SqlServer = "SqlServer";

    public const string PostgreSql = "PostgreSql";
}
