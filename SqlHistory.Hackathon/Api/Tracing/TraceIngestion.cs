using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SqlHistory.Hackathon.Api.Telemetry;

namespace SqlHistory.Hackathon.Api.Tracing;

/// <summary>
/// Batch of finished spans from one instrumented service. Field names follow OpenTelemetry
/// (trace id, span id, parent span id, kind, status, attributes, resource service.name).
/// </summary>
public sealed record SpanBatchRequest(string ServiceName, IReadOnlyList<SpanRequest> Spans);

public sealed record SpanRequest(
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string Name,
    string Kind,
    DateTime StartTimeUtc,
    double DurationMs,
    string Status,
    string? StatusDescription,
    IReadOnlyDictionary<string, string?>? Attributes);

public sealed record IngestResult(int Accepted, int Rejected, IReadOnlyList<string> Errors);

/// <summary>
/// Validates and stores spans. Only allow-listed attributes are kept: no SQL text, parameters,
/// URL paths or query strings, user identifiers or headers. Retention is bounded by age
/// (Tracing:RetentionHours) and count (Tracing:MaxSpans).
/// </summary>
public sealed partial class TraceIngestion
{
    public const int MaxSpansPerBatch = 500;
    private const int MaxValueLength = 256;

    public static readonly IReadOnlySet<string> AllowedAttributes = new HashSet<string>
    {
        "http.request.method", "http.route", "http.response.status_code",
        "db.system", "db.system.name", "db.namespace", "db.operation.name", "db.stored_procedure.name",
        "code.function.name", "code.namespace", "error.type",
        TraceAttributes.CallerType, TraceAttributes.JobName,
        TraceAttributes.RoutineSchema, TraceAttributes.RoutineName, TraceAttributes.RoutineArguments,
        TraceAttributes.RoutineType
    };

    private static readonly HashSet<string> Kinds = ["Internal", "Server", "Client", "Producer", "Consumer"];

    private readonly TelemetryDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly int _retentionHours;
    private readonly int _maxSpans;

    public TraceIngestion(TelemetryDbContext db, TimeProvider timeProvider, IConfiguration configuration)
    {
        _db = db;
        _timeProvider = timeProvider;
        _retentionHours = configuration.GetValue("Tracing:RetentionHours", 24);
        _maxSpans = configuration.GetValue("Tracing:MaxSpans", 20_000);
    }

    public async Task<IngestResult> IngestAsync(SpanBatchRequest batch, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var errors = new List<string>();
        var accepted = new List<SpanEntity>();

        if (string.IsNullOrWhiteSpace(batch.ServiceName) || batch.ServiceName.Length > 100)
            return new IngestResult(0, batch.Spans.Count, ["serviceName is required (at most 100 characters)."]);

        foreach (var span in batch.Spans.Take(MaxSpansPerBatch))
        {
            var error = Validate(span);
            if (error != null)
            {
                errors.Add($"{span.SpanId}: {error}");
                continue;
            }

            accepted.Add(ToEntity(batch.ServiceName, span, now));
        }

        if (batch.Spans.Count > MaxSpansPerBatch)
            errors.Add($"Only the first {MaxSpansPerBatch} spans of a batch are accepted.");

        // Exporters may retry; a span already stored is skipped rather than duplicated.
        var keys = accepted.Select(x => x.TraceId + x.SpanId).ToList();
        var existing = (await _db.Spans.Where(x => keys.Contains(x.TraceId + x.SpanId))
            .Select(x => x.TraceId + x.SpanId).ToListAsync(cancellationToken)).ToHashSet();
        var fresh = accepted.Where(x => !existing.Contains(x.TraceId + x.SpanId))
            .DistinctBy(x => x.TraceId + x.SpanId).ToList();

        _db.Spans.AddRange(fresh);
        await _db.SaveChangesAsync(cancellationToken);
        await PruneAsync(now, cancellationToken);

        return new IngestResult(fresh.Count, batch.Spans.Count - accepted.Count, errors.Take(20).ToList());
    }

    public static string? Validate(SpanRequest span)
    {
        if (!TraceIdRegex().IsMatch(span.TraceId ?? "")) return "traceId must be 32 lowercase hex characters.";
        if (!SpanIdRegex().IsMatch(span.SpanId ?? "")) return "spanId must be 16 lowercase hex characters.";
        if (span.ParentSpanId != null && !SpanIdRegex().IsMatch(span.ParentSpanId))
            return "parentSpanId must be 16 lowercase hex characters or null.";
        if (string.IsNullOrWhiteSpace(span.Name) || span.Name.Length > 200) return "name is required (at most 200 characters).";
        if (!Kinds.Contains(span.Kind)) return $"kind must be one of {string.Join(", ", Kinds)}.";
        if (span.DurationMs is < 0 or > 86_400_000) return "durationMs is out of range.";
        return null;
    }

    public static SpanEntity ToEntity(string serviceName, SpanRequest span, DateTime receivedAtUtc)
    {
        var attributes = (span.Attributes ?? new Dictionary<string, string?>())
            .Where(x => AllowedAttributes.Contains(x.Key) && x.Value != null)
            .ToDictionary(x => x.Key, x => Truncate(x.Value!));

        return new SpanEntity
        {
            TraceId = span.TraceId,
            SpanId = span.SpanId,
            ParentSpanId = span.ParentSpanId,
            Name = Truncate(span.Name),
            Kind = span.Kind,
            ServiceName = Truncate(serviceName),
            StartUtc = DateTime.SpecifyKind(span.StartTimeUtc, DateTimeKind.Utc),
            DurationMs = span.DurationMs,
            IsError = string.Equals(span.Status, "Error", StringComparison.OrdinalIgnoreCase),
            StatusDescription = span.StatusDescription == null ? null : Truncate(span.StatusDescription),
            HttpMethod = attributes.GetValueOrDefault("http.request.method"),
            HttpRoute = attributes.GetValueOrDefault("http.route"),
            HttpStatusCode = int.TryParse(attributes.GetValueOrDefault("http.response.status_code"), out var code)
                ? code
                : null,
            DeclaredCallerType = attributes.GetValueOrDefault(TraceAttributes.CallerType),
            RoutineSchema = attributes.GetValueOrDefault(TraceAttributes.RoutineSchema),
            RoutineName = attributes.GetValueOrDefault(TraceAttributes.RoutineName),
            RoutineArguments = attributes.GetValueOrDefault(TraceAttributes.RoutineArguments),
            RoutineType = attributes.GetValueOrDefault(TraceAttributes.RoutineType),
            AttributesJson = JsonSerializer.Serialize(attributes),
            ReceivedAtUtc = receivedAtUtc
        };
    }

    /// <summary>
    /// Removes whole traces, never single spans: deleting a trace's root while keeping its
    /// children would turn known callers into "unknown".
    /// </summary>
    private async Task PruneAsync(DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddHours(-_retentionHours);
        var traces = _db.Spans
            .GroupBy(x => x.TraceId)
            .Select(g => new { TraceId = g.Key, LastStartUtc = g.Max(x => x.StartUtc), Spans = g.Count() });

        var expired = traces.Where(x => x.LastStartUtc < cutoff).Select(x => x.TraceId);
        await _db.Spans.Where(x => expired.Contains(x.TraceId)).ExecuteDeleteAsync(cancellationToken);

        var overflow = await _db.Spans.CountAsync(cancellationToken) - _maxSpans;
        if (overflow <= 0) return;

        var removed = 0;
        var oldest = new List<string>();
        foreach (var trace in await traces.OrderBy(x => x.LastStartUtc).ToListAsync(cancellationToken))
        {
            if (removed >= overflow) break;
            oldest.Add(trace.TraceId);
            removed += trace.Spans;
        }

        foreach (var chunk in oldest.Chunk(500))
            await _db.Spans.Where(x => chunk.Contains(x.TraceId)).ExecuteDeleteAsync(cancellationToken);
    }

    private static string Truncate(string value) => value.Length <= MaxValueLength ? value : value[..MaxValueLength];

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex TraceIdRegex();

    [GeneratedRegex("^[0-9a-f]{16}$")]
    private static partial Regex SpanIdRegex();
}

/// <summary>Attribute names an application sets so SQL Time Machine can attribute a routine call.</summary>
public static class TraceAttributes
{
    /// <summary>"http_request", "internal_service" or "background_job", set on the root span.</summary>
    public const string CallerType = "sqlhistory.caller.type";

    public const string JobName = "sqlhistory.job.name";

    public const string RoutineSchema = "sqlhistory.routine.schema";

    public const string RoutineName = "sqlhistory.routine.name";

    /// <summary>Argument types as PostgreSQL prints them, e.g. "integer, date"; "" for none.</summary>
    public const string RoutineArguments = "sqlhistory.routine.arguments";

    /// <summary>"Function" or "Procedure".</summary>
    public const string RoutineType = "sqlhistory.routine.type";
}
