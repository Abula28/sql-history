namespace SqlHistory.Hackathon.AiAnalysis;

public enum RiskLevel
{
    None,
    Low,
    Medium,
    High
}

public sealed record SqlChangeAnalysis
{
    public required string Summary { get; init; }

    public required IReadOnlyList<string> Changes { get; init; }

    public required IReadOnlyList<string> Risks { get; init; }

    public required RiskLevel RiskLevel { get; init; }

    public required IReadOnlyList<string> Recommendations { get; init; }

    /// <summary>The model reported text inside the SQL that tried to instruct it, and ignored it.</summary>
    public required bool EmbeddedInstructionsDetected { get; init; }

    public required string Model { get; init; }
}

/// <summary>An analysis failure whose message is safe to show to the user.</summary>
public sealed class AiAnalysisException : Exception
{
    public AiAnalysisException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
