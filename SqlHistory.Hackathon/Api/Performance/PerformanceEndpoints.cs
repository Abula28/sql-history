using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SqlHistory.Core.Models;
using SqlHistory.Hackathon.Api.Endpoints;
using SqlHistory.Hackathon.Api.Telemetry;
using SqlHistory.PostgreSql.Routines;

namespace SqlHistory.Hackathon.Api.Performance;

public sealed record PerformanceStatusDto(
    bool StatementsAvailable,
    string? StatementsSetupHint,
    bool FunctionsTracked,
    string TrackFunctions,
    string? FunctionsSetupHint,
    DateTime? StatementsResetAtUtc,
    DateTime? LastSampleAtUtc,
    int SampleCount,
    int SampleIntervalSeconds);

public sealed record RoutineRefDto(string Id, string DisplayName);

public sealed record StatementDto(
    long QueryId,
    long UserId,
    string Role,
    bool TopLevel,
    string Query,
    long Calls,
    double TotalExecTimeMs,
    double MeanExecTimeMs,
    double MinExecTimeMs,
    double MaxExecTimeMs,
    long Rows,
    IReadOnlyList<RoutineRefDto> MentionsRoutines);

public sealed record StatementSummaryDto(
    int StatementCount,
    long TotalCalls,
    double TotalExecTimeMs,
    double MeanExecTimeMs,
    double MinExecTimeMs,
    double MaxExecTimeMs,
    long TotalRows);

public sealed record StatementsDto(
    bool Available,
    string? SetupHint,
    DateTime ReadAtUtc,
    DateTime? StatsResetAtUtc,
    StatementSummaryDto Summary,
    IReadOnlyList<StatementDto> Statements);

public sealed record FunctionStatDto(
    string? RoutineId,
    string DisplayName,
    string RoutineType,
    long Calls,
    double TotalTimeMs,
    double SelfTimeMs,
    double MeanTimeMs);

public sealed record FunctionsDto(bool Tracked, string TrackFunctions, string? SetupHint,
    DateTime? StatsResetAtUtc, IReadOnlyList<FunctionStatDto> Functions);

public sealed record RoutinePerformanceDto(
    bool FunctionsTracked,
    string? FunctionsSetupHint,
    FunctionStatDto? FunctionStats,
    TrendDto FunctionTrend,
    bool StatementsAvailable,
    IReadOnlyList<StatementDto> StatementsCallingRoutine);

public static class PerformanceEndpoints
{
    // Statements that only read system catalogs; nearly all of them are SQL Time Machine's own queries.
    private static readonly Regex CatalogQuery = new(@"\bpg_catalog\.|\bpg_stat_|\binformation_schema\.",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void MapPerformanceEndpoints(this IEndpointRouteBuilder app)
    {
        var performance = app.MapGroup("/api/performance").WithTags("Performance");

        performance.MapGet("/status", StatusAsync)
            .WithSummary("Which PostgreSQL statistics are enabled, with setup hints when they are not.");

        performance.MapGet("/statements", StatementsAsync)
            .WithSummary("pg_stat_statements for this database, cumulative since the last reset. Statement-level, not per routine.");

        performance.MapGet("/functions", FunctionsAsync)
            .WithSummary("pg_stat_user_functions: per-function call counts and times (needs track_functions). Sessions publish these with a delay of up to ~10 s.");

        performance.MapGet("/trends", TrendsAsync)
            .WithSummary("Activity per sampling interval, from consecutive stored samples.");

        performance.MapPost("/samples", SampleAsync)
            .AddEndpointFilter<RequireClientHeaderFilter>()
            .WithSummary("Take a statistics sample now.");

        app.MapGet("/api/routines/{id}/performance", RoutinePerformanceAsync)
            .WithTags("Performance")
            .WithSummary("Function statistics for one routine, plus statements whose text calls it (text match).")
            .Produces<RoutinePerformanceDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<PerformanceStatusDto> StatusAsync(PostgresStatsReader reader, TelemetryDbContext db,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        var availability = await reader.GetAvailabilityAsync(cancellationToken);
        var lastSample = await db.StatsSamples.AsNoTracking().OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var sampleCount = await db.StatsSamples.CountAsync(cancellationToken);

        return new PerformanceStatusDto(
            availability.StatementsAvailable,
            availability.StatementsSetupHint,
            availability.FunctionsTracked,
            availability.TrackFunctions,
            availability.FunctionsSetupHint,
            lastSample?.StatementsResetAtUtc,
            lastSample?.SampledAtUtc,
            sampleCount,
            configuration.GetValue("Performance:SampleIntervalSeconds", 30));
    }

    private static async Task<StatementsDto> StatementsAsync(PostgresStatsReader reader,
        PostgreSqlRoutineReader routineReader, string? sort, int? limit, bool? includeCatalog,
        CancellationToken cancellationToken)
    {
        var reading = await reader.ReadAsync(cancellationToken);
        var routines = await LiveRoutinesAsync(routineReader, cancellationToken);

        IEnumerable<StatementStat> statements = reading.Statements;
        if (includeCatalog != true)
            statements = statements.Where(x => !CatalogQuery.IsMatch(x.Query));
        var filtered = statements.ToList();

        var sorted = (sort?.ToLowerInvariant()) switch
        {
            "mean" => filtered.OrderByDescending(x => x.MeanExecTimeMs),
            "calls" => filtered.OrderByDescending(x => x.Calls),
            _ => filtered.OrderByDescending(x => x.TotalExecTimeMs)
        };

        var totalCalls = filtered.Sum(x => x.Calls);
        var totalTime = filtered.Sum(x => x.TotalExecTimeMs);
        var summary = new StatementSummaryDto(
            filtered.Count,
            totalCalls,
            totalTime,
            totalCalls == 0 ? 0 : totalTime / totalCalls,
            filtered.Count == 0 ? 0 : filtered.Min(x => x.MinExecTimeMs),
            filtered.Count == 0 ? 0 : filtered.Max(x => x.MaxExecTimeMs),
            filtered.Sum(x => x.Rows));

        return new StatementsDto(
            reading.Availability.StatementsAvailable,
            reading.Availability.StatementsSetupHint,
            reading.ReadAtUtc,
            reading.StatementsResetAtUtc,
            summary,
            sorted.Take(Math.Clamp(limit ?? 50, 1, 500)).Select(x => ToDto(x, routines)).ToList());
    }

    private static async Task<FunctionsDto> FunctionsAsync(PostgresStatsReader reader,
        CancellationToken cancellationToken)
    {
        var reading = await reader.ReadAsync(cancellationToken);
        return new FunctionsDto(
            reading.Availability.FunctionsTracked,
            reading.Availability.TrackFunctions,
            reading.Availability.FunctionsSetupHint,
            reading.DatabaseStatsResetAtUtc,
            reading.Functions.OrderByDescending(x => x.TotalTimeMs).Select(ToDto).ToList());
    }

    private static async Task<TrendDto> TrendsAsync(PerformanceTrends trends, TimeProvider timeProvider,
        double? hours, long? queryId, long? userId, bool? topLevel, CancellationToken cancellationToken)
    {
        var since = timeProvider.GetUtcNow().UtcDateTime.AddHours(-Math.Clamp(hours ?? 1, 0.05, 24));
        var key = queryId.HasValue && userId.HasValue
            ? new StatementKey(queryId.Value, userId.Value, topLevel ?? true)
            : null;
        return await trends.StatementsAsync(since, key, cancellationToken);
    }

    private static async Task<StatsSampleEntity> SampleAsync(PerformanceSampler sampler,
        CancellationToken cancellationToken)
    {
        return await sampler.TakeSampleAsync(cancellationToken);
    }

    private static async Task<IResult> RoutinePerformanceAsync(string id, PostgresStatsReader reader,
        PerformanceTrends trends, TimeProvider timeProvider, double? hours, CancellationToken cancellationToken)
    {
        if (!RoutineId.TryDecode(id, out var identity)) return RoutineEndpoints.InvalidId();

        var reading = await reader.ReadAsync(cancellationToken);
        var key = new FunctionKey(identity.SchemaName, identity.ProcedureName, identity.IdentityArguments);
        var function = reading.Functions.FirstOrDefault(x => x.Key == key);

        var pattern = RoutineMentions.Pattern(identity);
        var statements = reading.Statements
            .Where(x => pattern.IsMatch(x.Query))
            .OrderByDescending(x => x.TotalExecTimeMs)
            .Take(20)
            .Select(x => ToDto(x, [identity]))
            .ToList();

        var since = timeProvider.GetUtcNow().UtcDateTime.AddHours(-Math.Clamp(hours ?? 1, 0.05, 24));
        var trend = await trends.FunctionAsync(since, key, cancellationToken);

        return Results.Ok(new RoutinePerformanceDto(
            reading.Availability.FunctionsTracked,
            reading.Availability.FunctionsSetupHint,
            function == null ? null : ToDto(function),
            trend,
            reading.Availability.StatementsAvailable,
            statements));
    }

    private static async Task<IReadOnlyList<StoredProcedureIdentity>> LiveRoutinesAsync(
        PostgreSqlRoutineReader routineReader, CancellationToken cancellationToken)
    {
        return (await routineReader.GetStoredProceduresAsync(cancellationToken)).Select(x => x.Identity).ToList();
    }

    private static StatementDto ToDto(StatementStat x, IEnumerable<StoredProcedureIdentity> routines)
    {
        return new StatementDto(x.Key.QueryId, x.Key.UserId, x.Role, x.Key.TopLevel, x.Query, x.Calls,
            x.TotalExecTimeMs, x.MeanExecTimeMs, x.MinExecTimeMs, x.MaxExecTimeMs, x.Rows,
            RoutineMentions.Find(x.Query, routines)
                .Select(r => new RoutineRefDto(RoutineId.Encode(r), RoutineId.DisplayName(r))).ToList());
    }

    private static FunctionStatDto ToDto(FunctionStat x)
    {
        var type = x.Kind == 'p' ? RoutineType.Procedure : RoutineType.Function;
        var identity = new StoredProcedureIdentity(SourceProviders.PostgreSql, x.Key.SchemaName, x.Key.FunctionName,
            type, x.Key.IdentityArguments);

        return new FunctionStatDto(RoutineId.Encode(identity), RoutineId.DisplayName(identity), type.ToString(),
            x.Calls, x.TotalTimeMs, x.SelfTimeMs, x.Calls == 0 ? 0 : x.TotalTimeMs / x.Calls);
    }
}
