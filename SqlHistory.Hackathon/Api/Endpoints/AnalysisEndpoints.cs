using SqlHistory.Core.Abstractions;
using SqlHistory.Hackathon.AiAnalysis;
using SqlHistory.Hackathon.Api.Contracts;

namespace SqlHistory.Hackathon.Api.Endpoints;

public static class AnalysisEndpoints
{
    public static void MapAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/routines/{id}/analyze", AnalyzeAsync)
            .AddEndpointFilter<RequireClientHeaderFilter>()
            .WithTags("AI analysis")
            .WithSummary("Explain the change between two versions. Sends both definitions to the configured AI provider.")
            .Produces<AnalysisDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> AnalyzeAsync(string id, AnalyzeRequest request,
        IStoredProcedureHistoryRepository history, ISqlChangeAnalyzer analyzer, AiAnalysisOptions aiOptions,
        CancellationToken cancellationToken)
    {
        if (!RoutineId.TryDecode(id, out var identity)) return RoutineEndpoints.InvalidId();

        if (!analyzer.IsConfigured)
        {
            return Results.Problem(
                "AI analysis is not configured. Set Ai:ApiKey (or OPENAI_API_KEY) and restart the API.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var from = await history.GetVersionAsync(identity, request.From, cancellationToken);
        if (from == null) return RoutineEndpoints.VersionNotFound(request.From);

        var to = await history.GetVersionAsync(identity, request.To, cancellationToken);
        if (to == null) return RoutineEndpoints.VersionNotFound(request.To);

        if (from.DefinitionHash == to.DefinitionHash)
        {
            return Results.Problem($"Versions {from.VersionNumber} and {to.VersionNumber} are identical.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var analysis = await analyzer.AnalyzeAsync(RoutineId.DisplayName(identity),
                from.Definition ?? string.Empty, to.Definition ?? string.Empty, cancellationToken);

            return Results.Ok(new AnalysisDto(
                from.VersionNumber,
                to.VersionNumber,
                analysis.Summary,
                analysis.Changes,
                analysis.Risks,
                analysis.RiskLevel,
                analysis.Recommendations,
                analysis.EmbeddedInstructionsDetected,
                analysis.Model,
                new Uri(aiOptions.BaseUrl).Host));
        }
        catch (AiAnalysisException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
