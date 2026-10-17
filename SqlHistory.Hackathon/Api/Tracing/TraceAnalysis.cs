using SqlHistory.Hackathon.Api.Telemetry;

namespace SqlHistory.Hackathon.Api.Tracing;

public enum CallerType
{
    HttpRequest,
    InternalService,
    BackgroundJob,
    Unknown
}

/// <param name="Source">Where the classification came from, so the UI never presents a guess as fact.</param>
public sealed record CallerInfo(CallerType Type, string ServiceName, string Label, string? Endpoint, string Source);

/// <summary>
/// Classifies who started a trace using only the spans that were received. Rules, in order:
/// 1. The root span declares sqlhistory.caller.type: use it ("declared by the application").
/// 2. The root is a Server span with an HTTP method and no parent: an external HTTP request.
/// 3. The root is a Server span whose parent span was not received: called by another
///    (instrumented) service, so an internal service call.
/// 4. Anything else: unknown. Callers are never invented.
/// </summary>
public static class TraceAnalysis
{
    public static SpanEntity? FindRoot(IReadOnlyCollection<SpanEntity> spans)
    {
        var ids = spans.Select(x => x.SpanId).ToHashSet();
        return spans
            .Where(x => x.ParentSpanId == null || !ids.Contains(x.ParentSpanId))
            .OrderBy(x => x.ParentSpanId == null ? 0 : 1)
            .ThenBy(x => x.StartUtc)
            .FirstOrDefault();
    }

    public static CallerInfo Classify(IReadOnlyCollection<SpanEntity> spans)
    {
        var root = FindRoot(spans);
        if (root == null)
            return new CallerInfo(CallerType.Unknown, "unknown", "No spans received", null, "no data");

        var endpoint = root.HttpMethod != null ? $"{root.HttpMethod} {root.HttpRoute ?? "(unknown route)"}" : null;

        var declared = root.DeclaredCallerType switch
        {
            "http_request" => CallerType.HttpRequest,
            "internal_service" => CallerType.InternalService,
            "background_job" => CallerType.BackgroundJob,
            _ => (CallerType?)null
        };
        if (declared != null)
            return new CallerInfo(declared.Value, root.ServiceName, Label(root, endpoint), endpoint,
                "declared by the application (sqlhistory.caller.type)");

        if (root.Kind == "Server" && root.HttpMethod != null)
        {
            return root.ParentSpanId == null
                ? new CallerInfo(CallerType.HttpRequest, root.ServiceName, Label(root, endpoint), endpoint,
                    "HTTP server span with no parent")
                : new CallerInfo(CallerType.InternalService, root.ServiceName, Label(root, endpoint), endpoint,
                    "HTTP server span whose parent span (in another service) was not received");
        }

        return new CallerInfo(CallerType.Unknown, root.ServiceName, root.Name, endpoint,
            "root span has no HTTP or caller-type information");
    }

    /// <summary>The spans from the root down to <paramref name="span"/>, root first.</summary>
    public static IReadOnlyList<SpanEntity> PathTo(SpanEntity span, IReadOnlyCollection<SpanEntity> spans)
    {
        var byId = spans.ToDictionary(x => x.SpanId);
        var path = new List<SpanEntity> { span };
        var seen = new HashSet<string> { span.SpanId };

        var current = span;
        while (current.ParentSpanId != null && byId.TryGetValue(current.ParentSpanId, out var parent) &&
               seen.Add(parent.SpanId))
        {
            path.Add(parent);
            current = parent;
        }

        path.Reverse();
        return path;
    }

    public static bool IsRoutineCall(SpanEntity span) => span.RoutineSchema != null && span.RoutineName != null;

    private static string Label(SpanEntity root, string? endpoint) =>
        endpoint != null ? $"{root.ServiceName} / {endpoint}" : $"{root.ServiceName} / {root.Name}";
}
