namespace Tutor365.Infrastructure.AI;

/// <summary>Legacy appsettings section; live settings are in the database (AI.*). Kept for binding compatibility.</summary>
public class AiOptions
{
    public const string SectionName = "AI";
    public string Provider { get; set; } = "Stub";
    public string? ApiKey { get; set; }
    public string? Model { get; set; }
    public int MaxTokens { get; set; } = 800;
}
