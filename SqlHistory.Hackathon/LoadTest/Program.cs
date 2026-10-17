using System.Diagnostics;
using System.Net;
using System.Text.Json;

// Closed-loop HTTP load generator: N virtual users each send a request, wait for the answer,
// and send the next one, until the duration ends. Users start evenly spread over the ramp-up.
//
//   dotnet run --project SqlHistory.Hackathon/LoadTest -- --url http://localhost:5190/api/users/check \
//       --users 200 --duration 30 --ramp 5 [--out result.json]
//
// It sends no trace headers, so each request is an external HTTP request to the target.

var options = ParseArgs(args);
var url = new Uri(options.GetValueOrDefault("url") ?? "http://localhost:5190/api/users/check");
var users = int.Parse(options.GetValueOrDefault("users") ?? "200");
var duration = TimeSpan.FromSeconds(double.Parse(options.GetValueOrDefault("duration") ?? "30"));
var ramp = TimeSpan.FromSeconds(double.Parse(options.GetValueOrDefault("ramp") ?? "5"));
var output = options.GetValueOrDefault("out");

if (!url.IsLoopback)
{
    Console.Error.WriteLine($"Refusing to load-test {url.Host}: only localhost targets are allowed.");
    return 2;
}

using var client = new HttpClient(new SocketsHttpHandler
{
    MaxConnectionsPerServer = users,
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
}) { Timeout = TimeSpan.FromSeconds(30) };

var results = new List<Sample>[users];
var clock = Stopwatch.StartNew();
var end = ramp + duration;

Console.WriteLine($"{users} users → GET {url} for {duration.TotalSeconds:0}s (ramp-up {ramp.TotalSeconds:0}s)");

var tasks = Enumerable.Range(0, users).Select(async user =>
{
    var samples = results[user] = [];
    await Task.Delay(ramp * user / users);

    while (clock.Elapsed < end)
    {
        var started = clock.Elapsed;
        string outcome;
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead);
            outcome = response.StatusCode == HttpStatusCode.OK ? "ok" : $"HTTP {(int)response.StatusCode}";
        }
        catch (TaskCanceledException)
        {
            outcome = "timeout";
        }
        catch (HttpRequestException ex)
        {
            outcome = ex.HttpRequestError.ToString();
        }

        samples.Add(new Sample(started.TotalSeconds, (clock.Elapsed - started).TotalMilliseconds, outcome));
    }
}).ToArray();

await Task.WhenAll(tasks);

var all = results.SelectMany(x => x).ToList();
// Measure only the steady state: after every user has started.
var steady = all.Where(x => x.StartSeconds >= ramp.TotalSeconds).ToList();
var ok = steady.Where(x => x.Outcome == "ok").Select(x => x.LatencyMs).Order().ToList();

double Percentile(List<double> sorted, double p) =>
    sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p / 100 * sorted.Count) - 1)];

var summary = new
{
    url = url.ToString(),
    users,
    durationSeconds = duration.TotalSeconds,
    rampSeconds = ramp.TotalSeconds,
    totalRequests = all.Count,
    steadyRequests = steady.Count,
    steadyRequestsPerSecond = steady.Count / duration.TotalSeconds,
    successRate = steady.Count == 0 ? 0 : (double)ok.Count / steady.Count,
    errors = steady.Where(x => x.Outcome != "ok").GroupBy(x => x.Outcome).ToDictionary(g => g.Key, g => g.Count()),
    latencyMs = new
    {
        min = ok.Count == 0 ? 0 : ok[0],
        p50 = Percentile(ok, 50),
        p90 = Percentile(ok, 90),
        p95 = Percentile(ok, 95),
        p99 = Percentile(ok, 99),
        max = ok.Count == 0 ? 0 : ok[^1],
        mean = ok.Count == 0 ? 0 : ok.Average()
    },
    perSecond = all.GroupBy(x => (int)x.StartSeconds).OrderBy(g => g.Key).Select(g =>
    {
        var latencies = g.Where(x => x.Outcome == "ok").Select(x => x.LatencyMs).Order().ToList();
        return new { second = g.Key, requests = g.Count(), errors = g.Count(x => x.Outcome != "ok"), p95 = Percentile(latencies, 95) };
    })
};

var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
if (output != null) await File.WriteAllTextAsync(output, json);

Console.WriteLine($"steady state: {summary.steadyRequests} requests, {summary.steadyRequestsPerSecond:0} req/s, " +
                  $"{summary.successRate:P1} ok");
Console.WriteLine($"latency ms: p50 {summary.latencyMs.p50:0.0} · p90 {summary.latencyMs.p90:0.0} · " +
                  $"p95 {summary.latencyMs.p95:0.0} · p99 {summary.latencyMs.p99:0.0} · max {summary.latencyMs.max:0.0}");
if (summary.errors.Count > 0)
    Console.WriteLine("errors: " + string.Join(", ", summary.errors.Select(x => $"{x.Key} ×{x.Value}")));
return 0;

static Dictionary<string, string> ParseArgs(string[] args)
{
    var parsed = new Dictionary<string, string>();
    for (var i = 0; i + 1 < args.Length; i += 2)
        parsed[args[i].TrimStart('-')] = args[i + 1];
    return parsed;
}

internal sealed record Sample(double StartSeconds, double LatencyMs, string Outcome);
