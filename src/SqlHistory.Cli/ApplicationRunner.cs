using SqlHistory.Core.Abstractions;
using SqlHistory.Core.Models;
using SqlHistory.Core.Versioning;

namespace SqlHistory.Cli;

public sealed class ApplicationRunner
{
    private readonly IStoredProcedureReader _storedProcedureReader;
    private readonly StoredProcedureSnapshotFactory _storedProcedureSnapshotFactory;

    public ApplicationRunner(IStoredProcedureReader storedProcedureReader, StoredProcedureSnapshotFactory storedProcedureSnapshotFactory)
    {
        _storedProcedureReader = storedProcedureReader;
        _storedProcedureSnapshotFactory = storedProcedureSnapshotFactory;
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

        foreach (var procedure in procedures)
        {
            var snapshot =
                _storedProcedureSnapshotFactory.Create(procedure);

            Console.WriteLine(
                $"{snapshot.SchemaName}.{snapshot.ProcedureName}");

            Console.WriteLine(
                $"Hash: {snapshot.DefinitionHash[..12]}");

            Console.WriteLine(
                $"Modified: {snapshot.ModifiedAt}");

            Console.WriteLine(
                $"Captured: {snapshot.CapturedAtUtc} UTC");

            Console.WriteLine();
        }
    }
}