using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using OpenTelemetry;
using OpenTelemetry.Resources;

namespace SqlHistory.Hackathon.DemoApi.Telemetry;

/// <summary>
/// A small OpenTelemetry exporter that POSTs finished spans as JSON to SQL Time Machine
/// (POST /api/traces). Used instead of OTLP so SQL Time Machine needs no protobuf/OTLP receiver.
/// Spans that fail to export are dropped, not retried.
/// </summary>
public sealed class SqlTimeMachineExporter : BaseExporter<Activity>
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;

    public SqlTimeMachineExporter(Uri endpoint)
    {
        _endpoint = endpoint;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _httpClient.DefaultRequestHeaders.Add("X-SqlHistory-Client", "demo-api");
    }

    public override ExportResult Export(in Batch<Activity> batch)
    {
        // Our own HTTP call must not produce spans, or every export would create more to export.
        using var _ = SuppressInstrumentationScope.Begin();

        var spans = new List<object>();
        foreach (var activity in batch)
        {
            spans.Add(new
            {
                traceId = activity.TraceId.ToHexString(),
                spanId = activity.SpanId.ToHexString(),
                parentSpanId = activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
                name = activity.DisplayName,
                kind = activity.Kind.ToString(),
                startTimeUtc = activity.StartTimeUtc,
                durationMs = activity.Duration.TotalMilliseconds,
                status = activity.Status.ToString(),
                statusDescription = activity.StatusDescription,
                attributes = activity.TagObjects.ToDictionary(
                    x => x.Key,
                    x => Convert.ToString(x.Value, CultureInfo.InvariantCulture))
            });
        }

        try
        {
            using var response = _httpClient.PostAsJsonAsync(_endpoint, new { serviceName = ServiceName(), spans })
                .GetAwaiter().GetResult();
            return response.IsSuccessStatusCode ? ExportResult.Success : ExportResult.Failure;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ExportResult.Failure;
        }
    }

    private string ServiceName()
    {
        var resource = ParentProvider?.GetResource() ?? Resource.Empty;
        return resource.Attributes.FirstOrDefault(x => x.Key == "service.name").Value as string ?? "unknown_service";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _httpClient.Dispose();
        base.Dispose(disposing);
    }
}
