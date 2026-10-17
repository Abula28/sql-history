namespace SqlHistory.Hackathon.Api;

/// <summary>
/// Optional polling: scans every Scan:AutoScanIntervalSeconds. Disabled when the value is 0.
/// Changes are therefore detected on the next scan, not instantly.
/// </summary>
public sealed class AutoScanService : BackgroundService
{
    private readonly HistoryCoordinator _coordinator;
    private readonly ILogger<AutoScanService> _logger;
    private readonly int _intervalSeconds;

    public AutoScanService(HistoryCoordinator coordinator, IConfiguration configuration,
        ILogger<AutoScanService> logger)
    {
        _coordinator = coordinator;
        _logger = logger;
        _intervalSeconds = configuration.GetValue<int>("Scan:AutoScanIntervalSeconds");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_intervalSeconds <= 0)
            return;

        _logger.LogInformation("Auto-scan enabled every {Seconds} seconds.", _intervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));
        do
        {
            try
            {
                var result = await _coordinator.ScanAsync(stoppingToken);
                if (result.NewCount + result.ModifiedCount > 0)
                {
                    _logger.LogInformation("Auto-scan recorded {New} new and {Modified} modified routines.",
                        result.NewCount, result.ModifiedCount);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Auto-scan failed: {Message}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
