using Npgsql;
using SqlHistory.Core.Models;

namespace SqlHistory.PostgreSql.Routines;

/// <summary>
/// Catalog SQL shared by the reader and the restorer, so both see routines identically.
/// </summary>
internal static class PostgreSqlRoutineCatalog
{
    // Identity is (schema, name, kind, argument types). oidvectortypes(proargtypes) is the
    // same key PostgreSQL uses for its own uniqueness check, so overloads never collide and
    // renaming a parameter does not start a new history.
    //
    // Excluded: system schemas (names starting with pg_ are reserved), routines owned by an
    // extension, and aggregates/window functions, which pg_get_functiondef cannot render.
    private const string SelectRoutines = """
        SELECT
            p.oid::bigint AS object_id,
            n.nspname AS schema_name,
            p.proname AS routine_name,
            p.prokind AS routine_kind,
            pg_catalog.oidvectortypes(p.proargtypes) AS identity_arguments,
            pg_catalog.pg_get_functiondef(p.oid) AS definition
        FROM pg_catalog.pg_proc AS p
        INNER JOIN pg_catalog.pg_namespace AS n
            ON n.oid = p.pronamespace
        WHERE n.nspname NOT LIKE 'pg\_%'
          AND n.nspname <> 'information_schema'
          AND p.prokind IN ('f', 'p')
          AND NOT EXISTS (
              SELECT 1
              FROM pg_catalog.pg_depend AS d
              WHERE d.classid = 'pg_catalog.pg_proc'::regclass
                AND d.objid = p.oid
                AND d.deptype = 'e')
        """;

    public const string SelectAll = SelectRoutines + """

        ORDER BY n.nspname, p.proname, identity_arguments;
        """;

    public const string SelectByIdentity = SelectRoutines + """

          AND n.nspname = $1
          AND p.proname = $2
          AND p.prokind::text = $3
          AND pg_catalog.oidvectortypes(p.proargtypes) = $4;
        """;

    public const string CountUnsupported = """
        SELECT count(*)
        FROM pg_catalog.pg_proc AS p
        INNER JOIN pg_catalog.pg_namespace AS n
            ON n.oid = p.pronamespace
        WHERE n.nspname NOT LIKE 'pg\_%'
          AND n.nspname <> 'information_schema'
          AND p.prokind IN ('a', 'w');
        """;

    public static void AddIdentityParameters(NpgsqlCommand command, StoredProcedureIdentity identity)
    {
        command.Parameters.Add(new NpgsqlParameter { Value = identity.SchemaName });
        command.Parameters.Add(new NpgsqlParameter { Value = identity.ProcedureName });
        command.Parameters.Add(new NpgsqlParameter { Value = ToKind(identity.RoutineType) });
        command.Parameters.Add(new NpgsqlParameter { Value = identity.IdentityArguments });
    }

    public static StoredProcedureDefinition Read(NpgsqlDataReader reader)
    {
        return new StoredProcedureDefinition
        {
            ObjectId = reader.GetInt64(reader.GetOrdinal("object_id")),
            Provider = SourceProviders.PostgreSql,
            RoutineType = FromKind(reader.GetChar(reader.GetOrdinal("routine_kind"))),
            IdentityArguments = reader.GetString(reader.GetOrdinal("identity_arguments")),
            SchemaName = reader.GetString(reader.GetOrdinal("schema_name")),
            ProcedureName = reader.GetString(reader.GetOrdinal("routine_name")),
            Definition = reader.IsDBNull(reader.GetOrdinal("definition"))
                ? null
                : reader.GetString(reader.GetOrdinal("definition")),
            // pg_proc has no creation or modification timestamps.
            CreatedAt = null,
            ModifiedAt = null
        };
    }

    private static RoutineType FromKind(char kind) => kind switch
    {
        'f' => RoutineType.Function,
        'p' => RoutineType.Procedure,
        _ => throw new NotSupportedException($"Unsupported PostgreSQL routine kind '{kind}'.")
    };

    private static string ToKind(RoutineType routineType) => routineType switch
    {
        RoutineType.Function => "f",
        RoutineType.Procedure => "p",
        _ => throw new NotSupportedException($"Unsupported routine type '{routineType}'.")
    };
}
