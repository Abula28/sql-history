namespace SqlHistory.Hackathon.AiAnalysis;

public sealed class AiAnalysisOptions
{
    public const string SectionName = "Ai";

    /// <summary>Any OpenAI-compatible endpoint.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = "gpt-4.1-mini";

    /// <summary>
    /// Ask the provider for JSON-schema structured output. Turn off for OpenAI-compatible
    /// providers that do not support it; the answer is validated either way.
    /// </summary>
    public bool UseJsonSchema { get; set; } = true;

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Longer definitions are rejected rather than truncated, so the model never sees half a routine.</summary>
    public int MaxDefinitionLength { get; set; } = 40_000;
}
