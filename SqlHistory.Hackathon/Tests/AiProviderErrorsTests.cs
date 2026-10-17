using SqlHistory.Hackathon.AiAnalysis;

namespace SqlHistory.Hackathon.Tests;

public class AiProviderErrorsTests
{
    [Theory]
    [InlineData(429, "billing_not_active", "no active billing")]
    [InlineData(429, "insufficient_quota", "run out of credits")]
    [InlineData(429, "rate_limit_exceeded", "rate limit was hit")]
    [InlineData(429, null, "rate limit was hit")]
    [InlineData(401, "invalid_api_key", "rejected the API key")]
    [InlineData(404, "model_not_found", "does not offer the model 'gpt-x'")]
    [InlineData(503, null, "unavailable (HTTP 503)")]
    public void Describe_ShouldExplainTheActualFailure(int status, string? code, string expected)
    {
        var message = AiProviderErrors.Describe(status, code, "gpt-x", "https://api.openai.com/v1");

        Assert.Contains(expected, message);
    }

    [Fact]
    public void Describe_ShouldNotSuggestRetrying_WhenBillingIsInactive()
    {
        var message = AiProviderErrors.Describe(429, "billing_not_active", "gpt-x", "https://api.openai.com/v1");

        Assert.DoesNotContain("try again", message, StringComparison.OrdinalIgnoreCase);
    }
}
