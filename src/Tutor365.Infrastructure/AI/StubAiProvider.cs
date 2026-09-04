using Microsoft.Extensions.Logging;
using Tutor365.Application.Interfaces;

namespace Tutor365.Infrastructure.AI;

public class AiOptions
{
    public const string SectionName = "AI";
    /// <summary>"Stub" | "Anthropic" | "OpenAI"</summary>
    public string Provider { get; set; } = "Stub";
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public int MaxTokens { get; set; } = 800;
}

/// <summary>Placeholder provider used until a real AI vendor is configured. Returns curriculum-safe canned guidance.</summary>
public class StubAiProvider : IAiProvider
{
    private readonly ILogger<StubAiProvider> _logger;
    public StubAiProvider(ILogger<StubAiProvider> logger) => _logger = logger;

    public bool IsEnabled => false;
    public string ProviderName => "Stub";

    public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Stub AI provider invoked ({MessageCount} messages)", request.Messages.Count);
        const string content = "The AI tutor is not enabled yet. Re-read the explanation above, look at the worked example, and try breaking the question into smaller steps. Your tutor will be available soon.";
        return Task.FromResult(new AiCompletion(content, 0, 0, IsStub: true));
    }
}
