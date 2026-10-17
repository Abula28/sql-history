using System.Text.Json;

namespace SqlHistory.Hackathon.AiAnalysis;

/// <summary>
/// Turns the model's answer into a <see cref="SqlChangeAnalysis"/>. The answer is treated as
/// untrusted: it must be a JSON object with a summary and a known risk level, and every
/// string and list is capped in size.
/// </summary>
public static class SqlChangeAnalysisParser
{
    private const int MaxSummaryLength = 1_000;
    private const int MaxItemLength = 600;
    private const int MaxItems = 10;

    public static SqlChangeAnalysis Parse(string? content, string model)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new AiAnalysisException("The AI provider returned an empty answer.");

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(ExtractJsonObject(content));
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new AiAnalysisException("The AI provider returned an answer that is not valid JSON.", ex);
        }

        if (root.ValueKind != JsonValueKind.Object)
            throw new AiAnalysisException("The AI provider returned JSON that is not an object.");

        var summary = GetString(root, "summary");
        if (string.IsNullOrWhiteSpace(summary))
            throw new AiAnalysisException("The AI provider's answer has no summary.");

        var riskLevelText = GetString(root, "riskLevel");
        if (!Enum.TryParse<RiskLevel>(riskLevelText, ignoreCase: true, out var riskLevel)
            || !Enum.IsDefined(riskLevel))
        {
            throw new AiAnalysisException($"The AI provider returned an unknown risk level '{Truncate(riskLevelText ?? "", 20)}'.");
        }

        return new SqlChangeAnalysis
        {
            Summary = Truncate(summary.Trim(), MaxSummaryLength),
            Changes = GetStringList(root, "changes"),
            Risks = GetStringList(root, "risks"),
            RiskLevel = riskLevel,
            Recommendations = GetStringList(root, "recommendations"),
            EmbeddedInstructionsDetected =
                root.TryGetProperty("embeddedInstructionsDetected", out var flag) && flag.ValueKind == JsonValueKind.True,
            Model = model
        };
    }

    /// <summary>Accepts a bare object or one wrapped in prose or a ```json fence.</summary>
    private static string ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');

        return start >= 0 && end > start ? content[start..(end + 1)] : content;
    }

    private static string? GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static IReadOnlyList<string> GetStringList(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];

        return value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!.Trim())
            .Where(x => x.Length > 0)
            .Take(MaxItems)
            .Select(x => Truncate(x, MaxItemLength))
            .ToList();
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "…";
    }
}
