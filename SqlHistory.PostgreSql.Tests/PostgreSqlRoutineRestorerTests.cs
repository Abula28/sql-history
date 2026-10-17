using SqlHistory.Core.Models;
using SqlHistory.PostgreSql.Routines;

namespace SqlHistory.PostgreSql.Tests;

public class PostgreSqlRoutineRestorerTests
{
    [PostgresFact]
    public async Task Restore_ShouldApplyHistoricalDefinition_AndVerifyIt()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await CreateActiveUsersAsync(schema, "WHERE is_active");
        var v1 = await schema.CaptureVersionAsync("active_users", "", 1);
        await CreateActiveUsersAsync(schema, "");
        var v2 = await schema.CaptureVersionAsync("active_users", "", 2);

        var result = await schema.Restorer.RestoreAsync(v1, v2.DefinitionHash);

        var restored = await schema.CaptureVersionAsync("active_users", "", 3);
        Assert.Equal(RestoreStatus.Succeeded, result.Status);
        Assert.Equal(v1.DefinitionHash, restored.DefinitionHash);
        Assert.Equal(v1.ObjectId, restored.ObjectId);
        Assert.Equal(1L, await schema.ScalarAsync($"SELECT {schema.Name}.active_users()"));
    }

    [PostgresFact]
    public async Task Validate_ShouldDryRunWithoutChangingTheDatabase()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await CreateActiveUsersAsync(schema, "WHERE is_active");
        var v1 = await schema.CaptureVersionAsync("active_users", "", 1);
        await CreateActiveUsersAsync(schema, "");
        var v2 = await schema.CaptureVersionAsync("active_users", "", 2);

        var result = await schema.Restorer.ValidateAsync(v1);

        var current = await schema.CaptureVersionAsync("active_users", "", 3);
        Assert.Equal(RestoreStatus.Succeeded, result.Status);
        Assert.Equal(v2.DefinitionHash, result.CurrentDefinitionHash);
        Assert.Equal(v2.DefinitionHash, current.DefinitionHash);
    }

    [PostgresFact]
    public async Task Restore_ShouldRefuse_WhenRoutineChangedSincePreview()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await CreateActiveUsersAsync(schema, "WHERE is_active");
        var v1 = await schema.CaptureVersionAsync("active_users", "", 1);
        await CreateActiveUsersAsync(schema, "");
        var v2 = await schema.CaptureVersionAsync("active_users", "", 2);
        await CreateActiveUsersAsync(schema, "WHERE NOT is_active");
        var changedAfterPreview = await schema.CaptureVersionAsync("active_users", "", 3);

        var result = await schema.Restorer.RestoreAsync(v1, v2.DefinitionHash);

        var current = await schema.CaptureVersionAsync("active_users", "", 4);
        Assert.Equal(RestoreStatus.Conflict, result.Status);
        Assert.Equal(changedAfterPreview.DefinitionHash, current.DefinitionHash);
    }

    [PostgresFact]
    public async Task Restore_ShouldReportAlreadyCurrent_WhenNothingToRestore()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await CreateActiveUsersAsync(schema, "WHERE is_active");
        var v1 = await schema.CaptureVersionAsync("active_users", "", 1);

        var result = await schema.Restorer.ValidateAsync(v1);

        Assert.Equal(RestoreStatus.AlreadyCurrent, result.Status);
    }

    [PostgresFact]
    public async Task Restore_ShouldRefuse_ReturnTypeChange_WithoutDropping()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await schema.ExecuteAsync($"CREATE FUNCTION {schema.Name}.f() RETURNS integer LANGUAGE sql AS $$ SELECT 1 $$;");
        var v1 = await schema.CaptureVersionAsync("f", "", 1);
        await schema.ExecuteAsync($"""
            DROP FUNCTION {schema.Name}.f();
            CREATE FUNCTION {schema.Name}.f() RETURNS text LANGUAGE sql AS $$ SELECT 'one' $$;
            """);
        var v2 = await schema.CaptureVersionAsync("f", "", 2);

        var result = await schema.Restorer.RestoreAsync(v1, v2.DefinitionHash);

        Assert.Equal(RestoreStatus.NotSupported, result.Status);
        Assert.Contains("return type", result.Message);
        Assert.Equal("one", await schema.ScalarAsync($"SELECT {schema.Name}.f()"));
    }

    [PostgresFact]
    public async Task Restore_ShouldNeverRunASecondStatement()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await schema.ExecuteAsync($"""
            CREATE TABLE {schema.Name}.canary (id integer);
            CREATE FUNCTION {schema.Name}.f() RETURNS integer LANGUAGE sql AS $$ SELECT 2 $$;
            """);
        var current = await schema.CaptureVersionAsync("f", "", 2);

        // A tampered history row smuggling a second statement after the function body.
        var tampered = current with
        {
            VersionNumber = 1,
            Definition = $"CREATE OR REPLACE FUNCTION {schema.Name}.f() RETURNS integer LANGUAGE sql AS $$ SELECT 1 $$; DROP TABLE {schema.Name}.canary;",
            DefinitionHash = "TAMPERED"
        };

        var result = await schema.Restorer.RestoreAsync(tampered, current.DefinitionHash);

        // Refused by PostgreSQL before anything ran, not merely rolled back by the verification step.
        Assert.Equal(RestoreStatus.NotSupported, result.Status);
        Assert.Contains("cannot insert multiple commands into a prepared statement", result.Message);
        Assert.Equal(true, await schema.ScalarAsync($"SELECT to_regclass('{schema.Name}.canary') IS NOT NULL"));
        Assert.Equal(2, await schema.ScalarAsync($"SELECT {schema.Name}.f()"));
    }

    [PostgresFact]
    public async Task Restore_ShouldRefuse_DefinitionForAnotherRoutine()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await schema.ExecuteAsync($"CREATE FUNCTION {schema.Name}.f() RETURNS integer LANGUAGE sql AS $$ SELECT 2 $$;");
        var current = await schema.CaptureVersionAsync("f", "", 2);

        var otherRoutine = current with
        {
            VersionNumber = 1,
            Definition = $"CREATE OR REPLACE FUNCTION {schema.Name}.g() RETURNS integer LANGUAGE sql AS $$ SELECT 1 $$",
            DefinitionHash = "OTHER"
        };

        var result = await schema.Restorer.RestoreAsync(otherRoutine, current.DefinitionHash);

        Assert.Equal(RestoreStatus.NotSupported, result.Status);
        Assert.Equal(false, await schema.ScalarAsync($"SELECT to_regprocedure('{schema.Name}.g()') IS NOT NULL"));
    }

    [PostgresFact]
    public async Task Restore_ShouldRefuse_DroppedRoutine()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await schema.ExecuteAsync($"CREATE FUNCTION {schema.Name}.f() RETURNS integer LANGUAGE sql AS $$ SELECT 1 $$;");
        var v1 = await schema.CaptureVersionAsync("f", "", 1);
        await schema.ExecuteAsync($"DROP FUNCTION {schema.Name}.f();");

        var result = await schema.Restorer.ValidateAsync(v1);

        Assert.Equal(RestoreStatus.RoutineNotFound, result.Status);
    }

    [Fact]
    public void GetUnsupportedReason_ShouldRejectVersionsThatCannotBeRestored()
    {
        var valid = Version("CREATE OR REPLACE FUNCTION public.f() RETURNS integer LANGUAGE sql AS $$ SELECT 1 $$");

        Assert.Null(PostgreSqlRoutineRestorer.GetUnsupportedReason(valid));
        Assert.NotNull(PostgreSqlRoutineRestorer.GetUnsupportedReason(valid with { Provider = SourceProviders.SqlServer }));
        Assert.NotNull(PostgreSqlRoutineRestorer.GetUnsupportedReason(valid with { Definition = null }));
        Assert.NotNull(PostgreSqlRoutineRestorer.GetUnsupportedReason(valid with { Definition = "DROP FUNCTION public.f()" }));
        Assert.NotNull(PostgreSqlRoutineRestorer.GetUnsupportedReason(valid with { RoutineType = RoutineType.Procedure }));
    }

    /// <summary>Two users (one active) and an active_users() function with the given WHERE clause.</summary>
    private static Task CreateActiveUsersAsync(PostgresTestSchema schema, string filter)
    {
        return schema.ExecuteAsync($"""
            CREATE TABLE IF NOT EXISTS {schema.Name}.users AS SELECT * FROM (VALUES (true), (false)) AS v(is_active);
            CREATE OR REPLACE FUNCTION {schema.Name}.active_users() RETURNS bigint LANGUAGE sql
            AS $$ SELECT count(*) FROM {schema.Name}.users {filter} $$;
            """);
    }

    private static StoredProcedureVersion Version(string definition)
    {
        return new StoredProcedureVersion
        {
            Id = 1,
            VersionNumber = 1,
            ObjectId = 1,
            Provider = SourceProviders.PostgreSql,
            RoutineType = RoutineType.Function,
            IdentityArguments = "",
            SchemaName = "public",
            ProcedureName = "f",
            Definition = definition,
            DefinitionHash = "HASH",
            CreatedAt = null,
            ModifiedAt = null,
            CapturedAtUtc = DateTime.UtcNow
        };
    }
}
