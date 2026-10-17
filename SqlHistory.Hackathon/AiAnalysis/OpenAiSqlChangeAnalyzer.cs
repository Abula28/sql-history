using System.ClientModel;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace SqlHistory.Hackathon.AiAnalysis;

/// <summary>
/// Analyzes routine changes with any OpenAI-compatible chat completions endpoint.
/// Only the two routine definitions and the routine name are sent; never table data or
/// connection details. The model gets no tools, so it cannot act on the database.
/// </summary>
public sealed class OpenAiSqlChangeAnalyzer : ISqlChangeAnalyzer
{
    private readonly AiAnalysisOptions _options;
    private readonly ILogger<OpenAiSqlChangeAnalyzer> _logger;
    private readonly ChatClient? _client;

    public OpenAiSqlChangeAnalyzer(AiAnalysisOptions options, ILogger<OpenAiSqlChangeAnalyzer> logger)
    {
        _options = options;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            _client = new ChatClient(options.Model, new ApiKeyCredential(options.ApiKey), new OpenAIClientOptions
            {
                Endpoint = new Uri(options.BaseUrl),
                NetworkTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
            });
        }
    }

    public bool IsConfigured => _client != null;

    public async Task<SqlChangeAnalysis> AnalyzeAsync(string routineName, string previousDefinition,
        string currentDefinition, CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            throw new AiAnalysisException(
                "AI analysis is not configured. Set Ai:ApiKey (or OPENAI_API_KEY) and restart the API.");
        }

        if (previousDefinition.Length > _options.MaxDefinitionLength ||
            currentDefinition.Length > _options.MaxDefinitionLength)
        {
            throw new AiAnalysisException(
                $"The definitions are too long to analyze (limit {_options.MaxDefinitionLength} characters each).");
        }

        ChatMessage[] messages =
        [
            new SystemChatMessage(AnalysisPrompt.SystemMessage),
            new UserChatMessage(AnalysisPrompt.BuildUserMessage(routineName, previousDefinition, currentDefinition))
        ];

        var completionOptions = new ChatCompletionOptions();
        if (_options.UseJsonSchema)
        {
            completionOptions.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "sql_change_analysis", BinaryData.FromString(AnalysisPrompt.JsonSchema), jsonSchemaIsStrict: true);
        }

        ChatCompletion completion;
        try
        {
            completion = (await _client.CompleteChatAsync(messages, completionOptions, cancellationToken)).Value;
        }
        catch (ClientResultException ex)
        {
            var errorCode = AiProviderErrors.GetErrorCode(ex);
            _logger.LogWarning("AI provider returned HTTP {Status} ({ErrorCode}) for model {Model}.",
                ex.Status, errorCode ?? "no error code", _options.Model);
            throw new AiAnalysisException(
                AiProviderErrors.Describe(ex.Status, errorCode, _options.Model, _options.BaseUrl), ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiAnalysisException($"The AI provider did not answer within {_options.TimeoutSeconds} seconds.", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Could not reach the AI provider.");
            throw new AiAnalysisException("Could not reach the AI provider. Check the network and Ai:BaseUrl.", ex);
        }

        if (!string.IsNullOrEmpty(completion.Refusal))
            throw new AiAnalysisException($"The model declined to analyze this change: {completion.Refusal}");

        if (completion.FinishReason == ChatFinishReason.Length)
            throw new AiAnalysisException("The AI provider cut its answer off before it was complete.");

        var text = string.Concat(completion.Content.Select(x => x.Text));
        return SqlChangeAnalysisParser.Parse(text, completion.Model ?? _options.Model);
    }
}
