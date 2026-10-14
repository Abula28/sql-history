using SqlHistory.Core.Abstractions;
using SqlHistory.Core.Models;
using SqlHistory.Core.Versioning;

namespace SqlHistory.Cli;

public sealed class ApplicationRunner
{
    private readonly IStoredProcedureReader _storedProcedureReader;
    private readonly IStoredProcedureHistoryRepository _storedProcedureHistoryRepository;
    private readonly StoredProcedureSnapshotFactory _storedProcedureSnapshotFactory;
    private readonly StoredProcedureChangeDetector _storedProcedureChangeDetector;

    public ApplicationRunner(IStoredProcedureReader storedProcedureReader,
        IStoredProcedureHistoryRepository storedProcedureHistoryRepository,
        StoredProcedureSnapshotFactory storedProcedureSnapshotFactory,
        StoredProcedureChangeDetector storedProcedureChangeDetector)
    {
        _storedProcedureReader = storedProcedureReader;
        _storedProcedureHistoryRepository = storedProcedureHistoryRepository;
        _storedProcedureSnapshotFactory = storedProcedureSnapshotFactory;
        _storedProcedureChangeDetector = storedProcedureChangeDetector;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var procedures = await _storedProcedureReader
            .GetStoredProceduresAsync(cancellationToken);


        if (procedures.Count == 0)
        {
            Console.WriteLine("No stored procedures were found.");
            return;
        }

        Console.WriteLine(
            $"Found {procedures.Count} stored procedures.\n");

        var newProceduresCount = 0;
        var modifiedProceduresCount = 0;
        var unchangedProceduresCount = 0;
        var savedCount = 0;
        foreach (var procedure in procedures)
        {
            var snapshot =
                _storedProcedureSnapshotFactory.Create(procedure);

            var foundProc = await _storedProcedureHistoryRepository.GetLatestVersionAsync(snapshot.SchemaName,
                snapshot.ProcedureName, cancellationToken);

            var prevSnapshot = foundProc == null
                ? null
                : new StoredProcedureSnapshot
                {
                    ObjectId = foundProc.ObjectId,
                    SchemaName = foundProc.SchemaName,
                    ProcedureName = foundProc.ProcedureName,
                    Definition = foundProc.Definition,
                    DefinitionHash = foundProc.DefinitionHash,
                    CreatedAt = foundProc.CreatedAt,
                    ModifiedAt = foundProc.ModifiedAt,
                    CapturedAtUtc = foundProc.CapturedAtUtc
                };

            var detect = _storedProcedureChangeDetector.Detect(prevSnapshot, snapshot);

            switch (detect)
            {
                case ProcedureChangeStatus.New:
                    var result = await _storedProcedureHistoryRepository.AddVersionAsync(snapshot, cancellationToken);
                    newProceduresCount++;
                    savedCount++;
                    PrintInfo(result, ProcedureChangeStatus.New);
                    break;
                case ProcedureChangeStatus.Modified:
                    var res = await _storedProcedureHistoryRepository.AddVersionAsync(snapshot, cancellationToken);
                    modifiedProceduresCount++;
                    savedCount++;
                    PrintInfo(res, ProcedureChangeStatus.Modified);
                    break;
                case ProcedureChangeStatus.Unchanged:
                    unchangedProceduresCount++;
                    break;
            }
        }


        Console.WriteLine();
        Console.WriteLine($"New:\t{newProceduresCount}");
        Console.WriteLine($"Modified:\t{modifiedProceduresCount}");
        Console.WriteLine($"Unchanged:\t{unchangedProceduresCount}");
        Console.WriteLine($"Saved:\t{savedCount}");
    }


    private static void PrintInfo(StoredProcedureVersion snapshot, ProcedureChangeStatus status)
    {
        Console.WriteLine(
            $"[{status.ToString()}] {snapshot.SchemaName}.{snapshot.ProcedureName}");

        Console.WriteLine($"Version: {snapshot.VersionNumber}");

        Console.WriteLine(
            $"Hash: {snapshot.DefinitionHash[..12]}");

        Console.WriteLine();
    }
}