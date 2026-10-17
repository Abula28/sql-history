using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SqlHistory.Core.Models;
using SqlHistory.Hackathon.Api.Endpoints;
using SqlHistory.Hackathon.Api.Telemetry;
using SqlHistory.PostgreSql;

namespace SqlHistory.Hackathon.Api.Tracing;

public sealed record RoutineExecutionDto(
    string TraceId,
    string SpanId,
    DateTime StartUtc,
    double DurationMs,
    bool IsError,
    string? RoutineId,
    string RoutineDisplayName,
    CallerInfo Caller,
    IReadOnlyList<string> Path);

public sealed record TraceSpanDto(
    string SpanId,
    string? ParentSpanId,
    string Name,
    string Kind,
    string ServiceName,
    DateTime StartUtc,
    double DurationMs,
    bool IsError,
    string? StatusDescription,
    string? RoutineId,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record TraceDetailDto(string TraceId, CallerInfo Caller, IReadOnlyList<TraceSpanDto> Spans);

public sealed record CallerSummaryDto(CallerType Type, string Label, int Calls, double AverageDurationMs,
    DateTime LastCallUtc);

public sealed record RoutineExecutionsDto(
    int ObservedCalls,
    double? AverageDurationMs,
    DateTime? LastCallUtc,
    int ErrorCount,
    IReadOnlyList<CallerSummaryDto> Callers,
    IReadOnlyList<RoutineExecutionDto> Recent,
    int RetentionHours);

/// <summary>A current PostgreSQL session (pg_stat_activity). Query text is not exposed.</summary>
public sealed record DatabaseSessionDto(int Pid, string? ApplicationName, string? Role, string? ClientAddress,
    string? State, DateTime? BackendStartUtc, DateTime? StateChangeUtc, string? BackendType);

public static class TraceEndpoints
{
    public static void MapTraceEndpoints(this IEndpointRouteBuilder app)
    {
        var traces = app.MapGroup("/api/traces").WithTags("Tracing");

        traces.MapPost("/", IngestAsync)
            .AddEndpointFilter<RequireClientHeaderFilter>()
            .WithMetadata(new RequestSizeLimitAttribute(2_000_000))
            .WithSummary("Receive finished spans from an instrumented application (localhost only).");

        traces.MapGet("/", RecentAsync)
            .WithSummary("Recent routine executions reported by instrumented applications, with their caller.");

        traces.MapGet("/{traceId}", TraceAsync)
            .WithSummary("All received spans of one trace, with parent-child links.")
            .Produces<TraceDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/api/routines/{id}/executions", RoutineExecutionsAsync)
            .WithTags("Tracing")
            .WithSummary("Observed (instrumented) executions of one routine. Uninstrumented calls are not included.")
            .Produces<RoutineExecutionsDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        app.MapGet("/api/database/sessions", SessionsAsync)
            .WithTags("Tracing")
            .WithSummary("Current PostgreSQL sessions (pg_stat_activity): observed metadata, not request history.");
    }

    private static Task<IngestResult> IngestAsync(SpanBatchRequest batch, TraceIngestion ingestion,
        CancellationToken cancellationToken)
    {
        return ingestion.IngestAsync(batch, cancellationToken);
    }

    private static async Task<IReadOnlyList<RoutineExecutionDto>> RecentAsync(TelemetryDbContext db, int? limit,
        CancellationToken cancellationToken)
    {
        var calls = await db.Spans.AsNoTracking()
            .Where(x => x.RoutineSchema != null && x.RoutineName != null)
            .OrderByDescending(x => x.StartUtc)
            .Take(Math.Clamp(limit ?? 50, 1, 500))
            .ToListAsync(cancellationToken);

        return await WithCallersAsync(db, calls, cancellationToken);
    }

    private static async Task<IResult> TraceAsync(string traceId, TelemetryDbContext db,
        CancellationToken cancellationToken)
    {
        var spans = await db.Spans.AsNoTracking().Where(x => x.TraceId == traceId)
            .OrderBy(x => x.StartUtc).ToListAsync(cancellationToken);
        if (spans.Count == 0)
            return Results.Problem("No spans were received for this trace.", statusCode: StatusCodes.Status404NotFound);

        return Results.Ok(new TraceDetailDto(traceId, TraceAnalysis.Classify(spans), spans.Select(ToDto).ToList()));
    }

    private static async Task<IResult> RoutineExecutionsAsync(string id, TelemetryDbContext db,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!RoutineId.TryDecode(id, out var identity)) return RoutineEndpoints.InvalidId();

        var type = identity.RoutineType.ToString();
        var calls = await db.Spans.AsNoTracking()
            .Where(x => x.RoutineSchema == identity.SchemaName && x.RoutineName == identity.ProcedureName &&
                        (x.RoutineArguments ?? "") == identity.IdentityArguments &&
                        (x.RoutineType == null || x.RoutineType == type))
            .OrderByDescending(x => x.StartUtc)
            .ToListAsync(cancellationToken);

        var executions = await WithCallersAsync(db, calls, cancellationToken);
        var callers = executions
            .GroupBy(x => (x.Caller.Type, x.Caller.Label))
            .Select(g => new CallerSummaryDto(g.Key.Type, g.Key.Label, g.Count(), g.Average(x => x.DurationMs),
                g.Max(x => x.StartUtc)))
            .OrderByDescending(x => x.LastCallUtc)
            .ToList();

        return Results.Ok(new RoutineExecutionsDto(
            calls.Count,
            calls.Count == 0 ? null : calls.Average(x => x.DurationMs),
            calls.Count == 0 ? null : calls[0].StartUtc,
            calls.Count(x => x.IsError),
            callers,
            executions.Take(20).ToList(),
            configuration.GetValue("Tracing:RetentionHours", 24)));
    }

    private static async Task<IReadOnlyList<DatabaseSessionDto>> SessionsAsync(
        PostgreSqlConnectionFactory connectionFactory, CancellationToken cancellationToken)
    {
        await using var conn = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT pid, application_name, usename, host(client_addr), state, backend_start, state_change, backend_type
            FROM pg_catalog.pg_stat_activity
            WHERE datname = current_database() AND pid <> pg_backend_pid()
            ORDER BY backend_start;
            """, conn);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var sessions = new List<DatabaseSessionDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(new DatabaseSessionDto(
                reader.GetInt32(0),
                reader.IsDBNull(1) || reader.GetString(1) == "" ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5).ToUniversalTime(),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6).ToUniversalTime(),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return sessions;
    }

    private static async Task<IReadOnlyList<RoutineExecutionDto>> WithCallersAsync(TelemetryDbContext db,
        IReadOnlyList<SpanEntity> calls, CancellationToken cancellationToken)
    {
        var traceIds = calls.Select(x => x.TraceId).Distinct().ToList();
        var byTrace = (await db.Spans.AsNoTracking().Where(x => traceIds.Contains(x.TraceId))
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.TraceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<SpanEntity>)g.ToList());

        return calls.Select(call =>
        {
            var spans = byTrace.GetValueOrDefault(call.TraceId) ?? [call];
            var identity = RoutineIdentity(call);
            return new RoutineExecutionDto(
                call.TraceId,
                call.SpanId,
                call.StartUtc,
                call.DurationMs,
                call.IsError,
                identity == null ? null : RoutineId.Encode(identity),
                identity == null ? $"{call.RoutineSchema}.{call.RoutineName}" : RoutineId.DisplayName(identity),
                TraceAnalysis.Classify(spans),
                TraceAnalysis.PathTo(call, spans).Select(x => $"{x.ServiceName}: {x.Name}").ToList());
        }).ToList();
    }

    private static TraceSpanDto ToDto(SpanEntity x)
    {
        var identity = TraceAnalysis.IsRoutineCall(x) ? RoutineIdentity(x) : null;
        return new TraceSpanDto(x.SpanId, x.ParentSpanId, x.Name, x.Kind, x.ServiceName, x.StartUtc, x.DurationMs,
            x.IsError, x.StatusDescription, identity == null ? null : RoutineId.Encode(identity),
            JsonSerializer.Deserialize<Dictionary<string, string>>(x.AttributesJson) ?? []);
    }

    private static StoredProcedureIdentity? RoutineIdentity(SpanEntity span)
    {
        if (!TraceAnalysis.IsRoutineCall(span)) return null;
        var type = Enum.TryParse<RoutineType>(span.RoutineType, out var parsed) ? parsed : RoutineType.Function;
        return new StoredProcedureIdentity(SourceProviders.PostgreSql, span.RoutineSchema!, span.RoutineName!, type,
            span.RoutineArguments ?? "");
    }
}
