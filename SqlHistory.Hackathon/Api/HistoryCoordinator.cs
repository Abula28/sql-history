using SqlHistory.Core.Models;
using SqlHistory.Core.Services;
using SqlHistory.PostgreSql.Routines;

namespace SqlHistory.Hackathon.Api;

public sealed record RestoreOutcome(RestoreResult Result, ScanResult? ScanAfterRestore);

/// <summary>
/// Serializes everything that writes history, so manual scans, auto-scans and restores
/// never compute the same next version number at the same time. Also remembers the last scan.
/// </summary>
public sealed class HistoryCoordinator
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PostgreSqlRoutineRestorer _restorer;
    private readonly TimeProvider _timeProvider;

    public HistoryCoordinator(IServiceScopeFactory scopeFactory, PostgreSqlRoutineRestorer restorer,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _restorer = restorer;
        _timeProvider = timeProvider;
    }

    public ScanResult? LastScan { get; private set; }

    public DateTime? LastScanFailedAtUtc { get; private set; }

    public string? LastScanError { get; private set; }

    public async Task<ScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await ScanWhileLockedAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Scans first so any unrecorded change is saved before it is overwritten,
    /// restores, then scans again to record the restored definition as a new version.
    /// </summary>
    public async Task<RestoreOutcome> RestoreAsync(StoredProcedureVersion target, string expectedCurrentHash,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await ScanWhileLockedAsync(cancellationToken);

            var result = await _restorer.RestoreAsync(target, expectedCurrentHash, cancellationToken);
            if (result.Status != RestoreStatus.Succeeded)
                return new RestoreOutcome(result, null);

            // The restore is committed at this point; don't let a closed browser tab skip recording it.
            var scan = await ScanWhileLockedAsync(CancellationToken.None);
            return new RestoreOutcome(result, scan);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<ScanResult> ScanWhileLockedAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scanService = scope.ServiceProvider.GetRequiredService<SnapshotScanService>();

            var result = await scanService.ScanAsync(cancellationToken);
            LastScan = result;
            LastScanError = null;
            LastScanFailedAtUtc = null;
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastScanError = ex.Message;
            LastScanFailedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            throw;
        }
    }
}
