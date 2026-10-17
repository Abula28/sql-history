using SqlHistory.Hackathon.AiAnalysis;

namespace SqlHistory.Hackathon.Tests;

public class SqlChangeAnalysisParserTests
{
    private const string ValidAnswer = """
        {
          "summary": "The function no longer filters inactive users.",
          "changes": ["Removed WHERE is_active = true"],
          "risks": ["The count may now include inactive users."],
          "riskLevel": "medium",
          "recommendations": ["Verify whether removing the filter was intentional."],
          "embeddedInstructionsDetected": false
        }
        """;

    [Fact]
    public void Parse_ShouldMapAllFields()
    {
        var analysis = SqlChangeAnalysisParser.Parse(ValidAnswer, "test-model");

        Assert.Equal("The function no longer filters inactive users.", analysis.Summary);
        Assert.Equal(["Removed WHERE is_active = true"], analysis.Changes);
        Assert.Single(analysis.Risks);
        Assert.Equal(RiskLevel.Medium, analysis.RiskLevel);
        Assert.Single(analysis.Recommendations);
        Assert.False(analysis.EmbeddedInstructionsDetected);
        Assert.Equal("test-model", analysis.Model);
    }

    [Fact]
    public void Parse_ShouldAcceptJsonWrappedInMarkdownFence()
    {
        var analysis = SqlChangeAnalysisParser.Parse($"Here you go:\n```json\n{ValidAnswer}\n```", "m");

        Assert.Equal(RiskLevel.Medium, analysis.RiskLevel);
    }

    [Fact]
    public void Parse_ShouldReportEmbeddedInstructions()
    {
        var answer = ValidAnswer.Replace("\"embeddedInstructionsDetected\": false", "\"embeddedInstructionsDetected\": true");

        Assert.True(SqlChangeAnalysisParser.Parse(answer, "m").EmbeddedInstructionsDetected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I cannot help with that.")]
    [InlineData("""{ "riskLevel": "low" }""")]
    [InlineData("""{ "summary": "ok", "riskLevel": "catastrophic" }""")]
    [InlineData("""{ "summary": "ok", "riskLevel": "7" }""")]
    [InlineData("""["not", "an", "object"]""")]
    public void Parse_ShouldRejectUnusableAnswers(string? answer)
    {
        Assert.Throws<AiAnalysisException>(() => SqlChangeAnalysisParser.Parse(answer, "m"));
    }

    [Fact]
    public void Parse_ShouldCapListsAndStringLengths()
    {
        var items = string.Join(",", Enumerable.Range(0, 50).Select(i => $"\"{new string('x', 5000)}\""));
        var answer = $$"""{ "summary": "s", "riskLevel": "high", "changes": [{{items}}] }""";

        var analysis = SqlChangeAnalysisParser.Parse(answer, "m");

        Assert.Equal(10, analysis.Changes.Count);
        Assert.All(analysis.Changes, x => Assert.True(x.Length <= 601));
        Assert.Empty(analysis.Risks);
    }
}
