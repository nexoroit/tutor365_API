namespace Tutor365.Application.DTOs;

public record AiSettingsDto(bool Enabled, string Provider, bool HasApiKey, string? ApiKeyHint, string TutorModel, string MarkingModel, int DailyMessageLimit, int MaxTokens, bool AiMarkingEnabled, DateTime? UpdatedAt);
public record UpdateAiSettingsRequest(bool Enabled, string Provider, string? ApiKey, string TutorModel, string MarkingModel, int DailyMessageLimit, int MaxTokens, bool AiMarkingEnabled);
public record AiTestResponse(bool Ok, string Provider, string Model, string Reply, int InputTokens, int OutputTokens);
