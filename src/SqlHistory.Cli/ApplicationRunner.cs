using SqlHistory.Core.Abstractions;

namespace SqlHistory.Cli;

public sealed class ApplicationRunner
{
    private readonly IStoredProcedureReader _storedProcedureReader;

    public ApplicationRunner(IStoredProcedureReader storedProcedureReader)
    {
        _storedProcedureReader = storedProcedureReader;
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
            Console.WriteLine(
                $"{procedure.SchemaName}.{procedure.ProcedureName}");

            Console.WriteLine(
                $"Object ID: {procedure.ObjectId}");

            Console.WriteLine(
                $"Created: {procedure.CreatedAt}");

            Console.WriteLine(
                $"Modified: {procedure.ModifiedAt}");

            Console.WriteLine(
                $"Definition length: {procedure.Definition?.Length ?? 0}");

            Console.WriteLine();
        }
    }
}