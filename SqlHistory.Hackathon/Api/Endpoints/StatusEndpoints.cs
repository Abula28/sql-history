using Npgsql;
using SqlHistory.Core.Models;
using SqlHistory.Hackathon.AiAnalysis;
using SqlHistory.Hackathon.Api.Contracts;
using SqlHistory.PostgreSql;

namespace SqlHistory.Hackathon.Api.Endpoints;

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var status = app.MapGroup("/api").WithTags("Status");

        status.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithSummary("Liveness check; does not touch any database.");

        status.MapGet("/status", GetStatusAsync)
            .WithSummary("Source database connection, last scan and AI configuration.");
    }

    private static async Task<StatusDto> GetStatusAsync(
        PostgreSqlConnectionFactory connectionFactory,
        HistoryCoordinator coordinator,
        ISqlChangeAnalyzer analyzer,
        AiAnalysisOptions aiOptions,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        DatabaseStatusDto database;
        try
        {
            var info = await connectionFactory.GetServerInfoAsync(cancellationToken);
            database = new DatabaseStatusDto(SourceProviders.PostgreSql, true,
                info.Database, info.User, info.ServerVersion, null);
        }
        catch (NpgsqlException ex)
        {
            database = new DatabaseStatusDto(SourceProviders.PostgreSql, false,
                connectionFactory.DatabaseName, null, null, ex.Message);
        }

        var lastScan = coordinator.LastScan;
        var scan = new ScanStatusDto(
            lastScan?.ScannedAtUtc,
            lastScan == null ? null : ScanEndpoints.ToDto(lastScan),
            coordinator.LastScanFailedAtUtc,
            coordinator.LastScanError,
            configuration.GetValue<int>("Scan:AutoScanIntervalSeconds"));

        var ai = new AiStatusDto(analyzer.IsConfigured, aiOptions.Model, new Uri(aiOptions.BaseUrl).Host);

        return new StatusDto(database, scan, ai);
    }
}
