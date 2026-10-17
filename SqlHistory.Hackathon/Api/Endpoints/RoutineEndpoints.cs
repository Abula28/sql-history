using SqlHistory.Core.Abstractions;
using SqlHistory.Core.Models;
using SqlHistory.Hackathon.Api.Contracts;

namespace SqlHistory.Hackathon.Api.Endpoints;

public static class RoutineEndpoints
{
    public static void MapRoutineEndpoints(this IEndpointRouteBuilder app)
    {
        var routines = app.MapGroup("/api/routines").WithTags("Routines");

        routines.MapGet("/", ListAsync)
            .WithSummary("Routines in the database and in history, with their tracking state.")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        routines.MapGet("/{id}", GetAsync)
            .WithSummary("One routine with its current definition from the database.")
            .Produces<RoutineDetailDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        routines.MapGet("/{id}/versions", GetVersionsAsync)
            .WithSummary("Version timeline, newest first.")
            .Produces<IReadOnlyList<VersionSummaryDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        routines.MapGet("/{id}/versions/{version:int}", GetVersionAsync)
            .WithSummary("One historical version including its definition.")
            .Produces<VersionDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        routines.MapGet("/{id}/compare", CompareAsync)
            .WithSummary("Two versions with their definitions, for a diff.")
            .Produces<CompareDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static Task<IReadOnlyList<RoutineSummaryDto>> ListAsync(RoutineQueries queries,
        CancellationToken cancellationToken)
    {
        return queries.ListAsync(cancellationToken);
    }

    private static async Task<IResult> GetAsync(string id, RoutineQueries queries,
        CancellationToken cancellationToken)
    {
        if (!RoutineId.TryDecode(id, out var identity)) return InvalidId();

        var detail = await queries.GetAsync(identity, cancellationToken);
        return detail == null ? RoutineNotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> GetVersionsAsync(string id, IStoredProcedureHistoryRepository history,
        CancellationToken cancellationToken)
    {
        if (!RoutineId.TryDecode(id, out var identity)) return InvalidId();

        var versions = await history.GetVersionsAsync(identity, cancellationToken);

        // For each version, the first earlier version with the same content (e.g. after a restore).
        var firstVersionByHash = new Dictionary<string, int>();
        var identicalTo = new Dictionary<int, int?>();
        foreach (var version in versions.OrderBy(x => x.VersionNumber))
        {
            identicalTo[version.VersionNumber] =
                firstVersionByHash.TryGetValue(version.DefinitionHash, out var first) ? first : null;
            firstVersionByHash.TryAdd(version.DefinitionHash, version.VersionNumber);
        }

        return Results.Ok(versions.Select(x => new VersionSummaryDto(
            x.VersionNumber, x.CapturedAtUtc, x.DefinitionHash, identicalTo[x.VersionNumber])));
    }

    private static async Task<IResult> GetVersionAsync(string id, int version,
        IStoredProcedureHistoryRepository history, CancellationToken cancellationToken)
    {
        if (!RoutineId.TryDecode(id, out var identity)) return InvalidId();

        var found = await history.GetVersionAsync(identity, version, cancellationToken);
        return found == null ? VersionNotFound(version) : Results.Ok(ToDto(found));
    }

    private static async Task<IResult> CompareAsync(string id, int from, int to,
        IStoredProcedureHistoryRepository history, CancellationToken cancellationToken)
    {
        if (!RoutineId.TryDecode(id, out var identity)) return InvalidId();

        var fromVersion = await history.GetVersionAsync(identity, from, cancellationToken);
        if (fromVersion == null) return VersionNotFound(from);

        var toVersion = await history.GetVersionAsync(identity, to, cancellationToken);
        if (toVersion == null) return VersionNotFound(to);

        return Results.Ok(new CompareDto(ToDto(fromVersion), ToDto(toVersion)));
    }

    public static VersionDto ToDto(StoredProcedureVersion version)
    {
        return new VersionDto(version.VersionNumber, version.CapturedAtUtc, version.DefinitionHash,
            version.Definition);
    }

    public static IResult InvalidId() =>
        Results.Problem("The routine id is not valid.", statusCode: StatusCodes.Status400BadRequest);

    public static IResult RoutineNotFound() =>
        Results.Problem("The routine was not found in the database or in history.",
            statusCode: StatusCodes.Status404NotFound);

    public static IResult VersionNotFound(int version) =>
        Results.Problem($"Version {version} does not exist for this routine.",
            statusCode: StatusCodes.Status404NotFound);
}
