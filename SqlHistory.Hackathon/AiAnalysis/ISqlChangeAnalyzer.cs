namespace SqlHistory.Hackathon.AiAnalysis;

public interface ISqlChangeAnalyzer
{
    /// <summary>False when no API key is configured; AnalyzeAsync will then always fail.</summary>
    bool IsConfigured { get; }

    /// <exception cref="AiAnalysisException">The provider failed or returned an unusable answer.</exception>
    Task<SqlChangeAnalysis> AnalyzeAsync(
        string routineName,
        string previousDefinition,
        string currentDefinition,
        CancellationToken cancellationToken = default);
}
