using Microsoft.EntityFrameworkCore;
using SqlHistory.Hackathon.Api.Telemetry;

namespace SqlHistory.Hackathon.Api.Performance;

/// <summary>
/// Stores cumulative statistics samples so trends can be computed from consecutive samples.
/// Retention is bounded by age (Performance:RetentionHours) and count (Performance:MaxSamples).
/// </summary>
public sealed class PerformanceSampler
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PostgresStatsReader _reader;
    private readonly int _retentionHours;
    private readonly int _maxSamples;

    public PerformanceSampler(IServiceScopeFactory scopeFactory, PostgresStatsReader reader,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _reader = reader;
        _retentionHours = configuration.GetValue("Performance:RetentionHours", 24);
        _maxSamples = configuration.GetValue("Performance:MaxSamples", 2000);
    }

    public async Task<StatsSampleEntity> TakeSampleAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var reading = await _reader.ReadAsync(cancellationToken);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TelemetryDbContext>();

            var sample = new StatsSampleEntity
            {
                SampledAtUtc = reading.ReadAtUtc,
                StatementsAvailable = reading.Availability.StatementsAvailable,
                StatementsResetAtUtc = reading.StatementsResetAtUtc,
                StatementsDealloc = reading.StatementsDealloc,
                DatabaseStatsResetAtUtc = reading.DatabaseStatsResetAtUtc
            };
            db.StatsSamples.Add(sample);
            await db.SaveChangesAsync(cancellationToken);

            db.StatementSamples.AddRange(reading.Statements.Select(x => new StatementSampleEntity
            {
                SampleId = sample.Id,
                QueryId = x.Key.QueryId,
                UserId = x.Key.UserId,
                TopLevel = x.Key.TopLevel,
                Calls = x.Calls,
                TotalExecTimeMs = x.TotalExecTimeMs,
                Rows = x.Rows
            }));

            var queryIds = reading.Statements.Select(x => x.Key.QueryId).Distinct().ToList();
            var known = (await db.StatementTexts.Where(x => queryIds.Contains(x.QueryId))
                .Select(x => x.QueryId).ToListAsync(cancellationToken)).ToHashSet();
            db.StatementTexts.AddRange(reading.Statements
                .Where(x => !known.Contains(x.Key.QueryId))
                .DistinctBy(x => x.Key.QueryId)
                .Select(x => new StatementTextEntity { QueryId = x.Key.QueryId, Query = Truncate(x.Query, 4000) }));

            db.FunctionSamples.AddRange(reading.Functions.Select(x => new FunctionSampleEntity
            {
                SampleId = sample.Id,
                FunctionOid = x.FunctionOid,
                SchemaName = x.Key.SchemaName,
                FunctionName = x.Key.FunctionName,
                IdentityArguments = x.Key.IdentityArguments,
                Calls = x.Calls,
                TotalTimeMs = x.TotalTimeMs,
                SelfTimeMs = x.SelfTimeMs
            }));

            await db.SaveChangesAsync(cancellationToken);
            await PruneAsync(db, sample.SampledAtUtc, cancellationToken);
            return sample;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task PruneAsync(TelemetryDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        var cutoff = now.AddHours(-_retentionHours);
        var countCutoffId = await db.StatsSamples
            .OrderByDescending(x => x.Id)
            .Skip(_maxSamples)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;

        var expired = db.StatsSamples.Where(x => x.SampledAtUtc < cutoff || x.Id <= countCutoffId).Select(x => x.Id);

        await db.StatementSamples.Where(x => expired.Contains(x.SampleId)).ExecuteDeleteAsync(cancellationToken);
        await db.FunctionSamples.Where(x => expired.Contains(x.SampleId)).ExecuteDeleteAsync(cancellationToken);
        await db.StatsSamples.Where(x => x.SampledAtUtc < cutoff || x.Id <= countCutoffId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Samples every Performance:SampleIntervalSeconds (0 disables automatic sampling).</summary>
public sealed class PerformanceSamplingService : BackgroundService
{
    private readonly PerformanceSampler _sampler;
    private readonly ILogger<PerformanceSamplingService> _logger;
    private readonly int _intervalSeconds;

    public PerformanceSamplingService(PerformanceSampler sampler, IConfiguration configuration,
        ILogger<PerformanceSamplingService> logger)
    {
        _sampler = sampler;
        _logger = logger;
        _intervalSeconds = configuration.GetValue("Performance:SampleIntervalSeconds", 30);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_intervalSeconds <= 0) return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));
        do
        {
            try
            {
                await _sampler.TakeSampleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Performance sample failed: {Message}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
