using SqlHistory.Core.Models;

namespace SqlHistory.PostgreSql.Tests;

public class PostgreSqlRoutineReaderTests
{
    [PostgresFact]
    public async Task Reader_ShouldReturnFunctionsAndProcedures_WithDistinctOverloads()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await schema.ExecuteAsync($"""
            CREATE FUNCTION {schema.Name}.total(p_id integer) RETURNS integer LANGUAGE sql AS $$ SELECT p_id $$;
            CREATE FUNCTION {schema.Name}.total(p_id integer, p_rate numeric) RETURNS numeric LANGUAGE sql AS $$ SELECT p_id * p_rate $$;
            CREATE PROCEDURE {schema.Name}.refresh() LANGUAGE sql AS $$ SELECT 1 $$;
            """);

        var routines = await schema.ReadRoutinesAsync();

        Assert.Equal(3, routines.Count);
        Assert.Equal(3, routines.Select(x => x.Identity).Distinct().Count());
        Assert.Contains(routines, x => x is { ProcedureName: "total", IdentityArguments: "integer", RoutineType: RoutineType.Function });
        Assert.Contains(routines, x => x is { ProcedureName: "total", IdentityArguments: "integer, numeric" });
        Assert.Contains(routines, x => x is { ProcedureName: "refresh", IdentityArguments: "", RoutineType: RoutineType.Procedure });
        Assert.All(routines, x =>
        {
            Assert.Equal(SourceProviders.PostgreSql, x.Provider);
            Assert.StartsWith("CREATE OR REPLACE", x.Definition);
            Assert.Null(x.CreatedAt);
        });
    }

    [PostgresFact]
    public async Task Reader_ShouldSkipAggregates()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await schema.ExecuteAsync($"""
            CREATE FUNCTION {schema.Name}.add(a integer, b integer) RETURNS integer LANGUAGE sql AS $$ SELECT a + b $$;
            CREATE AGGREGATE {schema.Name}.my_sum(integer) (SFUNC = {schema.Name}.add, STYPE = integer, INITCOND = '0');
            """);

        var routines = await schema.ReadRoutinesAsync();

        Assert.Equal(["add"], routines.Select(x => x.ProcedureName));
    }

    [PostgresFact]
    public async Task Identity_ShouldUseArgumentTypes_NotParameterNames()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await schema.ExecuteAsync(
            $"CREATE FUNCTION {schema.Name}.f(p_customer_id integer, p_since date) RETURNS integer LANGUAGE sql AS $$ SELECT 1 $$;");
        var before = Assert.Single(await schema.ReadRoutinesAsync());

        // Renaming a parameter requires DROP + CREATE, but the identity must stay the same.
        await schema.ExecuteAsync($"""
            DROP FUNCTION {schema.Name}.f(integer, date);
            CREATE FUNCTION {schema.Name}.f(customer integer, since date) RETURNS integer LANGUAGE sql AS $$ SELECT 1 $$;
            """);
        var after = Assert.Single(await schema.ReadRoutinesAsync());

        Assert.Equal("integer, date", before.IdentityArguments);
        Assert.Equal(before.Identity, after.Identity);
        Assert.NotEqual(before.ObjectId, after.ObjectId);
        Assert.NotEqual(before.Definition, after.Definition);
    }

    [PostgresFact]
    public async Task Definition_ShouldBeStableAcrossReads()
    {
        await using var schema = await PostgresTestSchema.CreateAsync();
        await schema.ExecuteAsync(
            $"CREATE FUNCTION {schema.Name}.f() RETURNS integer LANGUAGE plpgsql AS $$ BEGIN RETURN 1; END $$;");

        var first = Assert.Single(await schema.ReadRoutinesAsync());
        var second = Assert.Single(await schema.ReadRoutinesAsync());

        Assert.Equal(
            schema.SnapshotFactory.Create(first).DefinitionHash,
            schema.SnapshotFactory.Create(second).DefinitionHash);
    }
}
