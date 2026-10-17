using SqlHistory.Core.Abstractions;
using SqlHistory.Hackathon.Api.Contracts;
using SqlHistory.PostgreSql.Routines;

namespace SqlHistory.Hackathon.Api.Endpoints;

/// <summary>
/// Restore is two deliberate steps: a preview that dry-runs the restore and shows the exact
/// SQL, then a confirmed request that must carry the hash of the definition the user saw.
/// Requests only name a version; the executed SQL always comes from stored history.
/// </summary>
public static class RollbackEndpoints
{
    public static void MapRollbackEndpoints(this IEndpointRouteBuilder app)
    {
        var rollback = app.MapGroup("/api/routines/{id}/rollback")
            .AddEndpointFilter<RequireClientHeaderFilter>()
            .WithTags("Rollback");

        rollback.MapPost("/preview", PreviewAsync)
            .WithSummary("Dry-run restoring a version inside a rolled-back transaction and return the exact SQL.")
            .Produces<RollbackPreviewDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        rollback.MapPost("/", RestoreAsync)
            .WithSummary("Restore a version. Requires confirm=true and the current hash returned by the preview.")
            .Produces<RollbackResultDto>()
            .Produces<RollbackResultDto>(StatusCodes.Status409Conflict)
            .Produces<RollbackResultDto>(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> PreviewAsync(string id, RollbackPreviewRequest request,
        IStoredProcedureHistoryRepository history, PostgreSqlRoutineRestorer restorer,
        CancellationToken cancellationToken)
    {
        if (!RoutineId.TryDecode(id, out var identity)) return RoutineEndpoints.InvalidId();

        var target = await history.GetVersionAsync(identity, request.Version, cancellationToken);
        if (target == null) return RoutineEndpoints.VersionNotFound(request.Version);

        var result = await restorer.ValidateAsync(target, cancellationToken);

        return Results.Ok(new RollbackPreviewDto(
            target.VersionNumber,
            target.Definition ?? string.Empty,
            result.CurrentDefinition,
            result.CurrentDefinitionHash,
            result.Status == RestoreStatus.Succeeded,
            result.Status,
            result.Message));
    }

    private static async Task<IResult> RestoreAsync(string id, RollbackRequest request,
        IStoredProcedureHistoryRepository history, HistoryCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        if (!RoutineId.TryDecode(id, out var identity)) return RoutineEndpoints.InvalidId();

        if (!request.Confirm)
        {
            return Results.Problem("Restoring requires explicit confirmation (confirm: true).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedCurrentHash))
        {
            return Results.Problem("expectedCurrentHash from the preview is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var target = await history.GetVersionAsync(identity, request.Version, cancellationToken);
        if (target == null) return RoutineEndpoints.VersionNotFound(request.Version);

        var outcome = await coordinator.RestoreAsync(target, request.ExpectedCurrentHash, cancellationToken);

        var recordedVersion = outcome.ScanAfterRestore?.Procedures
            .FirstOrDefault(x => x.Identity == identity)?.LatestVersion.VersionNumber;

        var statusCode = outcome.Result.Status switch
        {
            RestoreStatus.Succeeded => StatusCodes.Status200OK,
            RestoreStatus.Conflict or RestoreStatus.AlreadyCurrent => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return Results.Json(
            new RollbackResultDto(outcome.Result.Status, outcome.Result.Message, recordedVersion),
            statusCode: statusCode);
    }
}
