using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using AmIRite.Web.Models;

namespace AmIRite.Web.Services;

public class LlmService(LlmOptions options, ILogger<LlmService> logger)
{
    private AnthropicClient? _client;

    private AnthropicClient GetClient()
    {
        _client ??= new AnthropicClient(options.AnthropicApiKey);
        return _client;
    }

    // -- Decoy generation --

    public async Task<IReadOnlyList<string>> GenerateDecoysAsync(
        string questionText, string playerAnswer, int decoyCount)
    {
        var count = options.DecoyCountOverride ?? decoyCount;

        var systemPrompt =
            $"You are generating plausible fake answers to a personal question for a party game. " +
            $"The real answer provided by the player is given below. " +
            $"Generate exactly {count} alternative answers that: " +
            "- Are plausible responses to the question " +
            "- Match the approximate length, tone, and style of the real answer " +
            "- Are clearly distinct from the real answer and from each other " +
            "- Do not give away that they are fake " +
            "Return ONLY a JSON array of strings. No explanation, no preamble. " +
            """Example: ["answer one", "answer two", "answer three"]""";

        var request = new MessageParameters
        {
            Model = options.Model,
            MaxTokens = 512,
            Stream = false,
            System = new List<SystemMessage> { new(systemPrompt) },
            Messages = new List<Message>
            {
                new(RoleType.User, $"Question: {questionText}\nReal answer: {playerAnswer}")
            }
        };

        var response = await GetClient().Messages.GetClaudeMessageAsync(request);
        var raw = response.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? "[]";

        try
        {
            var decoys = JsonSerializer.Deserialize<List<string>>(raw.Trim()) ?? [];
            return decoys.Take(count).ToList();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse decoys JSON: {Raw}", raw);
            throw new InvalidOperationException("LLM returned invalid JSON for decoys", ex);
        }
    }

    // -- End-of-game analysis --

    public async Task<string> GenerateGameAnalysisAsync(object gameData)
    {
        var json = JsonSerializer.Serialize(gameData, new JsonSerializerOptions { WriteIndented = false });

        const string systemPrompt =
            "You are writing a fun, warm end-of-game summary for a two-player 'how well do you know each other' game. " +
            "Be specific, reference the actual questions and answers, and keep a light tone. " +
            "Structure your response as: " +
            "1. A 2-3 sentence overall narrative about how well the players know each other. " +
            "2. Category-level callouts: which categories each player excelled at or struggled with. " +
            "3. Highlight 1-2 specific moments. " +
            "4. A closing sentence that feels personal and warm. " +
            "Return plain HTML suitable for embedding in a page (use <p>, <strong>, <em> — no full document tags).";

        var request = new MessageParameters
        {
            Model = options.Model,
            MaxTokens = 1024,
            Stream = false,
            System = new List<SystemMessage> { new(systemPrompt) },
            Messages = new List<Message>
            {
                new(RoleType.User, json)
            }
        };

        var response = await GetClient().Messages.GetClaudeMessageAsync(request);
        return response.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? "";
    }
}
