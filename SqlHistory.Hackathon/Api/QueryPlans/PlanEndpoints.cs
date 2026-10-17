using Microsoft.EntityFrameworkCore;
using SqlHistory.Hackathon.Api.Contracts;
using SqlHistory.Hackathon.Api.Telemetry;
using SqlHistory.PostgreSql.Routines;

namespace SqlHistory.Hackathon.Api.QueryPlans;

public sealed record PlanQueryDto(string Key, string Title, string Description, string Sql,
    string? RoutineId, string? RoutineDisplayName);

public sealed record LivePlanDto(PlanQueryDto Query, DateTime ExplainedAtUtc, string Database,
    string ServerVersion, ParsedPlan Plan);

public sealed record PlanSnapshotDto(long Id, string QueryKey, DateTime CapturedAtUtc, string Database,
    string ServerVersion, string? RoutineDisplayName, int? RoutineVersion, double TotalCost);

public sealed record PlanSnapshotDetailDto(PlanSnapshotDto Snapshot, ParsedPlan Plan);

public sealed record PlanCompareDto(PlanSnapshotDetailDto From, PlanSnapshotDetailDto To, PlanDiffResult Diff);

public static class PlanEndpoints
{
    public static void MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        var plans = app.MapGroup("/api/plans").WithTags("Query plans");

        plans.MapGet("/", () => PlanQueryCatalog.Queries.Select(ToDto))
            .WithSummary("The predefined, reviewed queries that can be explained. Free-form SQL is not accepted.");

        plans.MapGet("/{key}/explain", ExplainAsync)
            .WithSummary("Estimated plan (EXPLAIN, never ANALYZE) in a read-only transaction. Not stored.")
            .Produces<LivePlanDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        plans.MapPost("/{key}/snapshots", CaptureAsync)
            .AddEndpointFilter<RequireClientHeaderFilter>()
            .WithSummary("Capture and store the current plan, with environment and routine version.")
            .Produces<PlanSnapshotDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        plans.MapGet("/{key}/snapshots", ListSnapshotsAsync)
            .WithSummary("Stored plan snapshots for a query, newest first.");

        plans.MapGet("/compare", CompareAsync)
            .WithSummary("Structural and estimated-cost differences between two stored snapshots.")
            .Produces<PlanCompareDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ExplainAsync(string key, PlanExplainer explainer, TimeProvider timeProvider,
        PostgreSqlRoutineReader reader, CancellationToken cancellationToken)
    {
        var query = PlanQueryCatalog.Find(key);
        if (query == null) return UnknownQuery(key);
        if (await MissingRoutineAsync(query, reader, cancellationToken) is { } missing) return missing;

        var result = await explainer.ExplainAsync(query, cancellationToken);
        return Results.Ok(new LivePlanDto(ToDto(query), timeProvider.GetUtcNow().UtcDateTime, result.Database,
            result.ServerVersion, PlanParser.Parse(result.PlanJson)));
    }

    private static async Task<IResult> CaptureAsync(string key, PlanExplainer explainer, RoutineQueries routines,
        PostgreSqlRoutineReader reader, TelemetryDbContext db, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var query = PlanQueryCatalog.Find(key);
        if (query == null) return UnknownQuery(key);
        if (await MissingRoutineAsync(query, reader, cancellationToken) is { } missing) return missing;

        var result = await explainer.ExplainAsync(query, cancellationToken);
        var plan = PlanParser.Parse(result.PlanJson);

        // Record the routine version only when the database still matches it; an unscanned
        // change would otherwise be attributed to the wrong version.
        int? routineVersion = null;
        if (query.Routine != null)
        {
            var routine = await routines.GetAsync(query.Routine, cancellationToken);
            if (routine?.Routine.State == RoutineState.Tracked)
                routineVersion = routine.Routine.LatestVersion;
        }

        var entity = new PlanSnapshotEntity
        {
            QueryKey = query.Key,
            CapturedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Database = result.Database,
            ServerVersion = result.ServerVersion,
            RoutineDisplayName = query.Routine == null ? null : RoutineId.DisplayName(query.Routine),
            RoutineVersion = routineVersion,
            TotalCost = plan.TotalCost,
            PlanJson = result.PlanJson
        };
        db.PlanSnapshots.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToDto(entity));
    }

    private static async Task<IReadOnlyList<PlanSnapshotDto>> ListSnapshotsAsync(string key, TelemetryDbContext db,
        CancellationToken cancellationToken)
    {
        var snapshots = await db.PlanSnapshots.AsNoTracking()
            .Where(x => x.QueryKey == key)
            .OrderByDescending(x => x.CapturedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);
        return snapshots.Select(ToDto).ToList();
    }

    private static async Task<IResult> CompareAsync(long from, long to, TelemetryDbContext db,
        CancellationToken cancellationToken)
    {
        var a = await db.PlanSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == from, cancellationToken);
        var b = await db.PlanSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == to, cancellationToken);
        if (a == null || b == null)
            return Results.Problem("Plan snapshot not found.", statusCode: StatusCodes.Status404NotFound);
        if (a.QueryKey != b.QueryKey)
            return Results.Problem("Only snapshots of the same query can be compared.",
                statusCode: StatusCodes.Status400BadRequest);

        var planA = PlanParser.Parse(a.PlanJson);
        var planB = PlanParser.Parse(b.PlanJson);
        return Results.Ok(new PlanCompareDto(
            new PlanSnapshotDetailDto(ToDto(a), planA),
            new PlanSnapshotDetailDto(ToDto(b), planB),
            PlanDiff.Compare(planA, planB)));
    }

    private static PlanQueryDto ToDto(PlanQuery query) =>
        new(query.Key, query.Title, query.Description, query.Sql,
            query.Routine == null ? null : RoutineId.Encode(query.Routine),
            query.Routine == null ? null : RoutineId.DisplayName(query.Routine));

    private static PlanSnapshotDto ToDto(PlanSnapshotEntity x) =>
        new(x.Id, x.QueryKey, x.CapturedAtUtc, x.Database, x.ServerVersion, x.RoutineDisplayName,
            x.RoutineVersion, x.TotalCost);

    private static async Task<IResult?> MissingRoutineAsync(PlanQuery query, PostgreSqlRoutineReader reader,
        CancellationToken cancellationToken)
    {
        if (query.Routine == null || await reader.GetRoutineAsync(query.Routine, cancellationToken) != null)
            return null;

        return Results.Problem(
            $"{RoutineId.DisplayName(query.Routine)} does not exist in the database (it may have been dropped), so this query cannot be planned.",
            statusCode: StatusCodes.Status409Conflict);
    }

    private static IResult UnknownQuery(string key) =>
        Results.Problem($"'{key}' is not one of the predefined plan queries.", statusCode: StatusCodes.Status404NotFound);
}
