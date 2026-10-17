using SqlHistory.Core.Services;
using SqlHistory.Core.Versioning;
using SqlHistory.Hackathon.Api.Contracts;

namespace SqlHistory.Hackathon.Api.Endpoints;

public static class ScanEndpoints
{
    public static void MapScanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/snapshots/scan", ScanAsync)
            .AddEndpointFilter<RequireClientHeaderFilter>()
            .WithTags("Snapshots")
            .WithSummary("Read every routine and record a new version for each changed definition.")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<ScanSummaryDto> ScanAsync(HistoryCoordinator coordinator,
        CancellationToken cancellationToken)
    {
        var result = await coordinator.ScanAsync(cancellationToken);
        return ToDto(result);
    }

    public static ScanSummaryDto ToDto(ScanResult result)
    {
        var changes = result.Procedures
            .Where(x => x.Status != ProcedureChangeStatus.Unchanged)
            .Select(x => new ScanChangeDto(
                RoutineId.Encode(x.Identity),
                RoutineId.DisplayName(x.Identity),
                x.Status.ToString(),
                x.LatestVersion.VersionNumber))
            .ToList();

        return new ScanSummaryDto(result.ScannedAtUtc, result.NewCount, result.ModifiedCount,
            result.UnchangedCount, changes);
    }
}
