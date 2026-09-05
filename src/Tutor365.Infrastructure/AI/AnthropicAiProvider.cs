using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Tutor365.Application.Interfaces;
using Tutor365.Application.Services;

namespace Tutor365.Infrastructure.AI;

/// <summary>Claude-backed provider. Settings (key, models, limits) come from the database; when disabled it returns a curriculum-safe stub reply.</summary>
public class AnthropicAiProvider : IAiProvider
{
    private readonly IAiSettingsService _settings;
    private readonly ILogger<AnthropicAiProvider> _logger;

    public AnthropicAiProvider(IAiSettingsService settings, ILogger<AnthropicAiProvider> logger) { _settings = settings; _logger = logger; }

    public string ProviderName => "Anthropic";

    public async Task<bool> IsEnabledAsync(CancellationToken ct = default)
    {
        var c = await _settings.GetConfigAsync(ct);
        return c.IsUsable && c.Provider == "Anthropic";
    }

    public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        var cfg = await _settings.GetConfigAsync(ct);
        if (!cfg.IsUsable || cfg.Provider != "Anthropic")
            return new AiCompletion(StubReply, 0, 0, IsStub: true);

        var model = request.Purpose == "marking" ? cfg.MarkingModel : cfg.TutorModel;
        var client = new AnthropicClient { ApiKey = cfg.ApiKey };

        // Messages must alternate and start with a user turn.
        var messages = new List<MessageParam>();
        foreach (var m in request.Messages)
        {
            var role = m.Role == "assistant" ? Role.Assistant : Role.User;
            if (messages.Count == 0 && role == Role.Assistant) continue;
            if (messages.Count > 0 && messages[^1].Role == role)
            {
                messages[^1] = new MessageParam { Role = role, Content = $"{messages[^1].Content}\n\n{m.Content}" };
                continue;
            }
            messages.Add(new MessageParam { Role = role, Content = m.Content });
        }
        if (messages.Count == 0) messages.Add(new MessageParam { Role = Role.User, Content = "Please help me." });

        try
        {
            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = model,
                MaxTokens = Math.Min(request.MaxTokens, cfg.MaxTokens),
                System = request.SystemPrompt,
                Messages = messages,
            }, cancellationToken: ct);

            var text = string.Join("\n", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Anthropic returned no text (stop reason {Stop})", response.StopReason);
                return new AiCompletion(StubReply, (int)(response.Usage?.InputTokens ?? 0), (int)(response.Usage?.OutputTokens ?? 0), IsStub: true, Model: model);
            }
            return new AiCompletion(text, (int)(response.Usage?.InputTokens ?? 0), (int)(response.Usage?.OutputTokens ?? 0), false, model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anthropic request failed (model {Model})", model);
            return new AiCompletion(StubReply, 0, 0, IsStub: true, Model: model);
        }
    }

    private const string StubReply = "The AI tutor isn't available right now. Re-read the explanation, look at the worked example, and try breaking the question into smaller steps.";
}
