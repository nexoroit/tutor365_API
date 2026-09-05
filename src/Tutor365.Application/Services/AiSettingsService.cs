using Microsoft.EntityFrameworkCore;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

public record AiConfig(bool Enabled, string Provider, string ApiKey, string TutorModel, string MarkingModel, int DailyMessageLimit, int MaxTokens, bool AiMarkingEnabled)
{
    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(ApiKey);
}

public interface IAiSettingsService
{
    Task<AiConfig> GetConfigAsync(CancellationToken ct = default);
    Task<AiSettingsDto> GetAsync(CancellationToken ct = default);
    Task<AiSettingsDto> UpdateAsync(UpdateAiSettingsRequest request, CancellationToken ct = default);
}

/// <summary>AI provider settings live in SystemSettings (AI.*) so admins can manage them; the API key is encrypted at rest and only ever shown as a hint.</summary>
public class AiSettingsService : IAiSettingsService
{
    public const string KeyPrefix = "AI.";
    public const string DefaultModel = "claude-opus-5";
    private static readonly string[] Providers = { "Anthropic", "Stub" };
    private readonly IAppDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IAuditService _audit;

    public AiSettingsService(IAppDbContext db, ISecretProtector protector, IAuditService audit) { _db = db; _protector = protector; _audit = audit; }

    public async Task<AiConfig> GetConfigAsync(CancellationToken ct = default)
    {
        var rows = await _db.SystemSettings.Where(s => s.Key.StartsWith(KeyPrefix)).ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        string Get(string k, string d = "") => rows.TryGetValue(KeyPrefix + k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : d;
        return new AiConfig(
            Get("Enabled", "false").Equals("true", StringComparison.OrdinalIgnoreCase),
            Get("Provider", "Anthropic"),
            _protector.Unprotect(Get("ApiKey")),
            Get("TutorModel", DefaultModel),
            Get("MarkingModel", DefaultModel),
            int.TryParse(Get("DailyMessageLimit", "40"), out var lim) ? lim : 40,
            int.TryParse(Get("MaxTokens", "1200"), out var mt) ? mt : 1200,
            Get("AiMarkingEnabled", "true").Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<AiSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var c = await GetConfigAsync(ct);
        var updated = await _db.SystemSettings.Where(s => s.Key.StartsWith(KeyPrefix)).MaxAsync(s => (DateTime?)s.UpdatedAt, ct);
        var hint = c.ApiKey.Length >= 8 ? $"{c.ApiKey[..7]}…{c.ApiKey[^4..]}" : null;
        return new AiSettingsDto(c.Enabled, c.Provider, c.ApiKey.Length > 0, hint, c.TutorModel, c.MarkingModel, c.DailyMessageLimit, c.MaxTokens, c.AiMarkingEnabled, updated);
    }

    public async Task<AiSettingsDto> UpdateAsync(UpdateAiSettingsRequest r, CancellationToken ct = default)
    {
        if (!Providers.Contains(r.Provider)) throw new AppValidationException("provider", $"Provider must be one of {string.Join(", ", Providers)}.");
        if (r.DailyMessageLimit is < 1 or > 1000) throw new AppValidationException("dailyMessageLimit", "Daily message limit must be between 1 and 1000.");
        if (r.MaxTokens is < 200 or > 8000) throw new AppValidationException("maxTokens", "Max tokens must be between 200 and 8000.");
        var values = new Dictionary<string, (string Value, string Desc)>
        {
            ["AI.Enabled"] = (r.Enabled ? "true" : "false", "Use the AI provider for tutoring and marking (false = rule-based only)."),
            ["AI.Provider"] = (r.Provider, "Anthropic | Stub."),
            ["AI.TutorModel"] = (string.IsNullOrWhiteSpace(r.TutorModel) ? DefaultModel : r.TutorModel.Trim(), "Model for hints, explanations and tutor chat."),
            ["AI.MarkingModel"] = (string.IsNullOrWhiteSpace(r.MarkingModel) ? DefaultModel : r.MarkingModel.Trim(), "Model for marking written answers."),
            ["AI.DailyMessageLimit"] = (r.DailyMessageLimit.ToString(), "Max tutor messages per student per day."),
            ["AI.MaxTokens"] = (r.MaxTokens.ToString(), "Max output tokens per reply."),
            ["AI.AiMarkingEnabled"] = (r.AiMarkingEnabled ? "true" : "false", "Mark short/long/exam answers with AI (falls back to keyword marking)."),
        };
        if (!string.IsNullOrWhiteSpace(r.ApiKey)) values["AI.ApiKey"] = (_protector.Protect(r.ApiKey.Trim()), "Provider API key (encrypted at rest).");
        foreach (var (key, (value, desc)) in values)
        {
            var row = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (row == null) { row = new SystemSetting { Key = key, IsPublic = false, Description = desc }; _db.SystemSettings.Add(row); }
            row.Value = value; row.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin.UpdateAiSettings", "SystemSetting", "AI", new { r.Enabled, r.Provider, r.TutorModel, r.MarkingModel, r.DailyMessageLimit, KeyChanged = !string.IsNullOrWhiteSpace(r.ApiKey) }, true, ct);
        return await GetAsync(ct);
    }
}
