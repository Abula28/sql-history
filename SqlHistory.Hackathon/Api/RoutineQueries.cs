using SqlHistory.Core.Abstractions;
using SqlHistory.Core.Models;
using SqlHistory.Core.Versioning;
using SqlHistory.Hackathon.Api.Contracts;
using SqlHistory.PostgreSql.Routines;

namespace SqlHistory.Hackathon.Api;

/// <summary>
/// Combines what is in the database right now with what is recorded in history,
/// so the explorer can show unscanned changes and dropped routines.
/// </summary>
public sealed class RoutineQueries
{
    private readonly PostgreSqlRoutineReader _reader;
    private readonly IStoredProcedureHistoryRepository _history;
    private readonly StoredProcedureSnapshotFactory _snapshotFactory;

    public RoutineQueries(PostgreSqlRoutineReader reader, IStoredProcedureHistoryRepository history,
        StoredProcedureSnapshotFactory snapshotFactory)
    {
        _reader = reader;
        _history = history;
        _snapshotFactory = snapshotFactory;
    }

    public async Task<IReadOnlyList<RoutineSummaryDto>> ListAsync(CancellationToken cancellationToken)
    {
        var live = await _reader.GetStoredProceduresAsync(cancellationToken);
        var latestVersions = (await _history.GetLatestVersionsAsync(SourceProviders.PostgreSql, cancellationToken))
            .ToDictionary(x => x.Identity);

        var routines = new List<RoutineSummaryDto>();

        foreach (var routine in live)
        {
            latestVersions.Remove(routine.Identity, out var latest);
            routines.Add(Summarize(routine.Identity, latest, Hash(routine)));
        }

        // Whatever is left was recorded once but is no longer in the database.
        routines.AddRange(latestVersions.Values.Select(x => Summarize(x.Identity, x, currentHash: null)));

        return routines
            .OrderBy(x => x.Schema, StringComparer.Ordinal)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.IdentityArguments, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<RoutineDetailDto?> GetAsync(StoredProcedureIdentity identity,
        CancellationToken cancellationToken)
    {
        var live = await _reader.GetRoutineAsync(identity, cancellationToken);
        var latest = await _history.GetLatestVersionAsync(identity, cancellationToken);

        if (live == null && latest == null) return null;

        var currentHash = live == null ? null : Hash(live);
        return new RoutineDetailDto(Summarize(identity, latest, currentHash), live?.Definition, currentHash);
    }

    /// <param name="currentHash">Hash of the live definition, or null if the routine is not in the database.</param>
    private static RoutineSummaryDto Summarize(StoredProcedureIdentity identity, StoredProcedureVersion? latest,
        string? currentHash)
    {
        var state =
            currentHash == null ? RoutineState.Dropped
            : latest == null ? RoutineState.Untracked
            : latest.DefinitionHash == currentHash ? RoutineState.Tracked
            : RoutineState.ChangedSinceLastScan;

        return new RoutineSummaryDto(
            RoutineId.Encode(identity),
            identity.SchemaName,
            identity.ProcedureName,
            identity.RoutineType,
            identity.IdentityArguments,
            RoutineId.DisplayName(identity),
            state,
            latest?.VersionNumber,
            latest?.CapturedAtUtc);
    }

    private string Hash(StoredProcedureDefinition definition)
    {
        return _snapshotFactory.Create(definition).DefinitionHash;
    }
}
