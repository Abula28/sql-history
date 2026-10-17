using Microsoft.EntityFrameworkCore;
using SqlHistory.Hackathon.Api.Telemetry;

namespace SqlHistory.Hackathon.Api.Performance;

public sealed record TrendPointDto(
    DateTime FromUtc,
    DateTime ToUtc,
    long Calls,
    double TotalTimeMs,
    double MeanTimeMs,
    bool StatsReset,
    bool PossiblyIncomplete);

public sealed record TrendStatementDto(long QueryId, long UserId, bool TopLevel, string Query, long Calls,
    double TotalTimeMs, double MeanTimeMs);

public sealed record TrendDto(int SampleCount, IReadOnlyList<TrendPointDto> Points,
    IReadOnlyList<TrendStatementDto> TopStatements);

/// <summary>Builds per-interval activity from consecutive stored samples.</summary>
public sealed class PerformanceTrends
{
    private readonly TelemetryDbContext _db;

    public PerformanceTrends(TelemetryDbContext db)
    {
        _db = db;
    }

    /// <param name="key">Only this statement; null for all statements of the database.</param>
    public async Task<TrendDto> StatementsAsync(DateTime sinceUtc, StatementKey? key, CancellationToken cancellationToken)
    {
        // Samples taken while pg_stat_statements was unavailable are skipped rather than read as zero.
        var samples = await _db.StatsSamples.AsNoTracking()
            .Where(x => x.SampledAtUtc >= sinceUtc && x.StatementsAvailable)
            .OrderBy(x => x.SampledAtUtc)
            .ToListAsync(cancellationToken);
        var sampleIds = samples.Select(x => x.Id).ToList();

        var rows = _db.StatementSamples.AsNoTracking().Where(x => sampleIds.Contains(x.SampleId));
        if (key != null)
            rows = rows.Where(x => x.QueryId == key.QueryId && x.UserId == key.UserId && x.TopLevel == key.TopLevel);
        var bySample = (await rows.ToListAsync(cancellationToken)).ToLookup(x => x.SampleId);

        var snapshots = samples.Select(s => new StatsSnapshot<StatementKey>(
            s.SampledAtUtc,
            s.StatementsResetAtUtc,
            s.StatementsDealloc,
            bySample[s.Id].ToDictionary(
                x => new StatementKey(x.QueryId, x.UserId, x.TopLevel),
                x => new StatCounters(x.Calls, x.TotalExecTimeMs, x.Rows)))).ToList();

        var intervals = Intervals(snapshots);

        var totals = intervals.SelectMany(x => x.Deltas)
            .GroupBy(x => x.Key)
            .Select(g => (Key: g.Key, Calls: g.Sum(x => x.Calls), Time: g.Sum(x => x.TotalTimeMs)))
            .OrderByDescending(x => x.Time)
            .Take(10)
            .ToList();
        var ids = totals.Select(x => x.Key.QueryId).ToList();
        var texts = await _db.StatementTexts.AsNoTracking().Where(x => ids.Contains(x.QueryId))
            .ToDictionaryAsync(x => x.QueryId, x => x.Query, cancellationToken);

        return new TrendDto(
            samples.Count,
            intervals.Select(ToPoint).ToList(),
            totals.Select(x => new TrendStatementDto(x.Key.QueryId, x.Key.UserId, x.Key.TopLevel,
                texts.GetValueOrDefault(x.Key.QueryId, "<unknown>"), x.Calls, x.Time,
                x.Calls == 0 ? 0 : x.Time / x.Calls)).ToList());
    }

    public async Task<TrendDto> FunctionAsync(DateTime sinceUtc, FunctionKey key, CancellationToken cancellationToken)
    {
        var samples = await _db.StatsSamples.AsNoTracking()
            .Where(x => x.SampledAtUtc >= sinceUtc)
            .OrderBy(x => x.SampledAtUtc)
            .ToListAsync(cancellationToken);
        var sampleIds = samples.Select(x => x.Id).ToList();

        var bySample = (await _db.FunctionSamples.AsNoTracking()
                .Where(x => sampleIds.Contains(x.SampleId) && x.SchemaName == key.SchemaName &&
                            x.FunctionName == key.FunctionName && x.IdentityArguments == key.IdentityArguments)
                .ToListAsync(cancellationToken))
            .ToLookup(x => x.SampleId);

        var snapshots = samples.Select(s => new StatsSnapshot<FunctionKey>(
            s.SampledAtUtc,
            s.DatabaseStatsResetAtUtc,
            null,
            bySample[s.Id].ToDictionary(
                x => new FunctionKey(x.SchemaName, x.FunctionName, x.IdentityArguments),
                x => new StatCounters(x.Calls, x.TotalTimeMs, SelfTimeMs: x.SelfTimeMs)))).ToList();

        return new TrendDto(samples.Count, Intervals(snapshots).Select(ToPoint).ToList(), []);
    }

    private static List<StatsInterval<TKey>> Intervals<TKey>(List<StatsSnapshot<TKey>> snapshots) where TKey : notnull
    {
        var intervals = new List<StatsInterval<TKey>>();
        for (var i = 1; i < snapshots.Count; i++)
            intervals.Add(StatsDelta.Compute(snapshots[i - 1], snapshots[i]));
        return intervals;
    }

    private static TrendPointDto ToPoint<TKey>(StatsInterval<TKey> interval)
    {
        return new TrendPointDto(interval.FromUtc, interval.ToUtc, interval.TotalCalls, interval.TotalTimeMs,
            interval.TotalCalls == 0 ? 0 : interval.TotalTimeMs / interval.TotalCalls,
            interval.StatsReset, interval.PossiblyIncomplete);
    }
}
