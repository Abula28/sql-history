using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SqlHistory.Hackathon.Api.Telemetry;
using SqlHistory.Hackathon.Api.Tracing;

namespace SqlHistory.Hackathon.Tests;

/// <summary>Retention against a real (in-memory) SQLite telemetry store.</summary>
public sealed class TraceRetentionTests : IAsyncDisposable
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    [Fact]
    public async Task CountCap_ShouldRemoveWholeOldestTraces_NeverSplitThem()
    {
        await using var db = await CreateDbAsync();
        var ingestion = Ingestion(db, maxSpans: 7);

        // Three traces of three spans each; the cap of 7 forces one trace out.
        foreach (var (trace, minutesAgo) in new[] { ("a", 30), ("b", 20), ("c", 10) })
            await ingestion.IngestAsync(Batch(trace, Now.AddMinutes(-minutesAgo)), CancellationToken.None);

        var remaining = await db.Spans.AsNoTracking().ToListAsync();
        Assert.Equal(6, remaining.Count);
        Assert.DoesNotContain(remaining, x => x.TraceId == TraceId("a"));
        Assert.All(remaining.GroupBy(x => x.TraceId), g => Assert.Equal(3, g.Count()));
    }

    [Fact]
    public async Task CountCap_ShouldKeepEveryRootSpan_SoCallersStayKnown()
    {
        await using var db = await CreateDbAsync();
        var ingestion = Ingestion(db, maxSpans: 10);

        for (var i = 0; i < 8; i++)
            await ingestion.IngestAsync(Batch($"t{i}", Now.AddMinutes(-60 + i)), CancellationToken.None);

        var spans = await db.Spans.AsNoTracking().ToListAsync();
        foreach (var trace in spans.GroupBy(x => x.TraceId))
            Assert.Equal(CallerType.HttpRequest, TraceAnalysis.Classify(trace.ToList()).Type);
    }

    [Fact]
    public async Task AgeLimit_ShouldKeepTracesWhoseNewestSpanIsRecent()
    {
        await using var db = await CreateDbAsync();
        var ingestion = Ingestion(db, maxSpans: 1000, retentionHours: 1);

        await ingestion.IngestAsync(Batch("old", Now.AddHours(-3)), CancellationToken.None);
        // Starts before the cutoff but its last span is inside the window.
        await ingestion.IngestAsync(Batch("straddling", Now.AddHours(-1).AddSeconds(-1), spanGapSeconds: 2), CancellationToken.None);

        var traces = await db.Spans.AsNoTracking().Select(x => x.TraceId).Distinct().ToListAsync();
        Assert.Equal([TraceId("straddling")], traces);
        Assert.Equal(3, await db.Spans.CountAsync());
    }

    private async Task<TelemetryDbContext> CreateDbAsync()
    {
        await _connection.OpenAsync();
        var db = new TelemetryDbContext(new DbContextOptionsBuilder<TelemetryDbContext>().UseSqlite(_connection).Options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static TraceIngestion Ingestion(TelemetryDbContext db, int maxSpans, int retentionHours = 24)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tracing:MaxSpans"] = maxSpans.ToString(),
            ["Tracing:RetentionHours"] = retentionHours.ToString()
        }).Build();
        return new TraceIngestion(db, new FixedTimeProvider(Now), configuration);
    }

    /// <summary>An HTTP request trace: server span → service span → routine call.</summary>
    private static SpanBatchRequest Batch(string name, DateTime start, double spanGapSeconds = 0.001)
    {
        var trace = TraceId(name);
        SpanRequest Span(int index, string? parent, string spanName, string kind, Dictionary<string, string?> attributes) =>
            new(trace, SpanId(name, index), parent, spanName, kind, start.AddSeconds(index * spanGapSeconds), 2, "Ok", null,
                attributes);

        return new SpanBatchRequest("UsersApi",
        [
            Span(0, null, "GET /api/users", "Server",
                new() { ["http.request.method"] = "GET", ["http.route"] = "/api/users" }),
            Span(1, SpanId(name, 0), "UsersService.GetAllUsers", "Internal", new()),
            Span(2, SpanId(name, 1), "public.get_all_users()", "Client",
                new() { [TraceAttributes.RoutineSchema] = "public", [TraceAttributes.RoutineName] = "get_all_users" })
        ]);
    }

    private static string TraceId(string name) => Hex(name, 32);

    private static string SpanId(string name, int index) => Hex(name + index, 16);

    private static string Hex(string seed, int length)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)));
        return hash[..length].ToLowerInvariant();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
