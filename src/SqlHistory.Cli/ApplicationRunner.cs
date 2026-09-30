using SqlHistory.Core.Abstractions;

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

        foreach (var procedure in procedures)
        {
            Console.WriteLine(
                $"{procedure.SchemaName}.{procedure.ProcedureName}");
        }
    }
}