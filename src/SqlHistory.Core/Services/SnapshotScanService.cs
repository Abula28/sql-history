using SqlHistory.Core.Abstractions;
using SqlHistory.Core.Models;
using SqlHistory.Core.Versioning;

namespace SqlHistory.Core.Services;

public sealed record ScannedProcedure(
    StoredProcedureIdentity Identity,
    ProcedureChangeStatus Status,
    StoredProcedureVersion LatestVersion);

public sealed record ScanResult(DateTime ScannedAtUtc, IReadOnlyList<ScannedProcedure> Procedures)
{
    public int NewCount => Procedures.Count(x => x.Status == ProcedureChangeStatus.New);

    public int ModifiedCount => Procedures.Count(x => x.Status == ProcedureChangeStatus.Modified);

    public int UnchangedCount => Procedures.Count(x => x.Status == ProcedureChangeStatus.Unchanged);
}

/// <summary>
/// Reads every routine from the source database and stores a new version
/// for each one whose definition differs from its latest stored version.
/// Callers are responsible for not running two scans against the same history at once.
/// </summary>
public class SnapshotScanService
{
    private readonly IStoredProcedureReader _reader;
    private readonly IStoredProcedureHistoryRepository _historyRepository;
    private readonly StoredProcedureSnapshotFactory _snapshotFactory;
    private readonly StoredProcedureChangeDetector _changeDetector;
    private readonly TimeProvider _timeProvider;

    public SnapshotScanService(IStoredProcedureReader reader,
        IStoredProcedureHistoryRepository historyRepository,
        StoredProcedureSnapshotFactory snapshotFactory,
        StoredProcedureChangeDetector changeDetector,
        TimeProvider timeProvider)
    {
        _reader = reader;
        _historyRepository = historyRepository;
        _snapshotFactory = snapshotFactory;
        _changeDetector = changeDetector;
        _timeProvider = timeProvider;
    }

    public async Task<ScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        var scannedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var procedures = await _reader.GetStoredProceduresAsync(cancellationToken);

        var results = new List<ScannedProcedure>(procedures.Count);

        foreach (var procedure in procedures)
        {
            var snapshot = _snapshotFactory.Create(procedure);

            var latest = await _historyRepository.GetLatestVersionAsync(snapshot.Identity, cancellationToken);

            var status = _changeDetector.Detect(latest == null ? null : ToSnapshot(latest), snapshot);

            if (status == ProcedureChangeStatus.Unchanged)
            {
                results.Add(new ScannedProcedure(snapshot.Identity, status, latest!));
                continue;
            }

            var saved = await _historyRepository.AddVersionAsync(snapshot, cancellationToken);
            results.Add(new ScannedProcedure(snapshot.Identity, status, saved));
        }

        return new ScanResult(scannedAtUtc, results);
    }

    private static StoredProcedureSnapshot ToSnapshot(StoredProcedureVersion version)
    {
        return new StoredProcedureSnapshot
        {
            ObjectId = version.ObjectId,
            Provider = version.Provider,
            RoutineType = version.RoutineType,
            IdentityArguments = version.IdentityArguments,
            SchemaName = version.SchemaName,
            ProcedureName = version.ProcedureName,
            Definition = version.Definition,
            DefinitionHash = version.DefinitionHash,
            CreatedAt = version.CreatedAt,
            ModifiedAt = version.ModifiedAt,
            CapturedAtUtc = version.CapturedAtUtc
        };
    }
}
