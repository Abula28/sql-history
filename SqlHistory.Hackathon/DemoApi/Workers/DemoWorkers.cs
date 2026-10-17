using SqlHistory.Hackathon.DemoApi.Services;
using SqlHistory.Hackathon.DemoApi.Telemetry;

namespace SqlHistory.Hackathon.DemoApi.Workers;

/// <summary>Background job: calls public.get_all_users() in-process on a timer.</summary>
public sealed class UserSyncWorker : BackgroundService
{
    private readonly UsersService _usersService;
    private readonly ILogger<UserSyncWorker> _logger;
    private readonly TimeSpan _interval;

    public UserSyncWorker(UsersService usersService, IConfiguration configuration, ILogger<UserSyncWorker> logger)
    {
        _usersService = usersService;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(configuration.GetValue("Workers:UserSyncIntervalSeconds", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_interval <= TimeSpan.Zero) return;

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        using var timer = new PeriodicTimer(_interval);
        do
        {
            using var activity = DemoTelemetry.StartRoot("UserSyncWorker.Run", "background_job");
            try
            {
                var users = await _usersService.GetAllUsersAsync(stoppingToken);
                _logger.LogInformation("UserSyncWorker synced {Count} users.", users.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.GetType().Name);
                _logger.LogWarning("UserSyncWorker failed: {Message}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>
/// Internal service: calls this API's own HTTP endpoint like a separate service would. The trace
/// context travels in the W3C traceparent header, so the HTTP request joins the caller's trace.
/// </summary>
public sealed class BillingService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BillingService> _logger;
    private readonly TimeSpan _interval;

    public BillingService(IHttpClientFactory httpClientFactory, IConfiguration configuration,
        ILogger<BillingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(configuration.GetValue("Workers:BillingIntervalSeconds", 40));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_interval <= TimeSpan.Zero) return;

        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
        using var timer = new PeriodicTimer(_interval);
        do
        {
            using var activity = DemoTelemetry.StartRoot("BillingService.RefreshSeatCount", "internal_service");
            try
            {
                var client = _httpClientFactory.CreateClient("self");
                var response = await client.GetStringAsync("/api/users/active-count", stoppingToken);
                _logger.LogInformation("BillingService refreshed seat count: {Response}", response);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.GetType().Name);
                _logger.LogWarning("BillingService failed: {Message}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
