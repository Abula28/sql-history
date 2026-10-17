using System.ClientModel;
using System.Text.Json;

namespace SqlHistory.Hackathon.AiAnalysis;

/// <summary>
/// Turns provider HTTP failures into actionable messages. HTTP 429 in particular covers both
/// temporary rate limits and accounts that cannot be billed, which need very different fixes.
/// </summary>
public static class AiProviderErrors
{
    public static string Describe(int status, string? errorCode, string model, string baseUrl) => (status, errorCode) switch
    {
        (429, "billing_not_active") =>
            "The AI provider account behind this API key has no active billing. " +
            "Activate billing or add credits in the provider's dashboard (for OpenAI: platform.openai.com → Billing).",
        (429, "insufficient_quota") =>
            "The AI provider account has run out of credits or quota. Add credits or raise the limit in the provider's dashboard.",
        (429, _) => "The AI provider's rate limit was hit. Wait a moment and try again.",
        (401 or 403, _) => "The AI provider rejected the API key.",
        (404, _) => $"The AI provider does not offer the model '{model}' at {baseUrl}.",
        (400, _) => "The AI provider rejected the request. If it does not support JSON-schema output, set Ai:UseJsonSchema to false.",
        (>= 500, _) => $"The AI provider is unavailable (HTTP {status}). Try again.",
        _ => $"The AI provider returned HTTP {status}."
    };

    /// <summary>Reads error.code (or error.type) from an OpenAI-style error body, if there is one.</summary>
    public static string? GetErrorCode(ClientResultException exception)
    {
        try
        {
            var content = exception.GetRawResponse()?.Content;
            if (content == null) return null;

            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var name in new[] { "code", "type" })
            {
                if (error.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
