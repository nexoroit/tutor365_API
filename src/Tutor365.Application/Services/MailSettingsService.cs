using Microsoft.EntityFrameworkCore;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

/// <summary>Resolved SMTP configuration (from SystemSettings). Security: None | StartTls | StartTlsWhenAvailable | Ssl.</summary>
public record MailConfig(bool Enabled, string Host, int Port, string Security, string Username, string Password, string FromEmail, string FromName);

public interface IMailSettingsService
{
    Task<MailConfig?> GetConfigAsync(CancellationToken ct = default);
    Task<MailSettingsDto> GetAsync(CancellationToken ct = default);
    Task<MailSettingsDto> UpdateAsync(UpdateMailSettingsRequest request, CancellationToken ct = default);
}

public class MailSettingsService : IMailSettingsService
{
    public const string KeyPrefix = "Smtp.";
    private static readonly string[] Securities = { "None", "StartTls", "StartTlsWhenAvailable", "Ssl" };
    private readonly IAppDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly IAuditService _audit;

    public MailSettingsService(IAppDbContext db, ISecretProtector protector, IAuditService audit) { _db = db; _protector = protector; _audit = audit; }

    public async Task<MailConfig?> GetConfigAsync(CancellationToken ct = default)
    {
        var rows = await _db.SystemSettings.Where(s => s.Key.StartsWith(KeyPrefix)).ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        if (rows.Count == 0 || !rows.TryGetValue("Smtp.Host", out var host) || string.IsNullOrWhiteSpace(host)) return null;
        string Get(string k, string d = "") => rows.TryGetValue(KeyPrefix + k, out var v) ? v : d;
        return new MailConfig(Get("Enabled", "false").Equals("true", StringComparison.OrdinalIgnoreCase), host, int.TryParse(Get("Port", "25"), out var p) ? p : 25,
            Get("Security", "StartTlsWhenAvailable"), Get("Username"), _protector.Unprotect(Get("Password")), Get("FromEmail"), Get("FromName", "tutor365"));
    }

    public async Task<MailSettingsDto> GetAsync(CancellationToken ct = default)
    {
        var cfg = await GetConfigAsync(ct);
        var updated = await _db.SystemSettings.Where(s => s.Key.StartsWith(KeyPrefix)).MaxAsync(s => (DateTime?)s.UpdatedAt, ct);
        return cfg == null
            ? new MailSettingsDto(false, "", 25, "StartTlsWhenAvailable", "", false, "", "tutor365", null)
            : new MailSettingsDto(cfg.Enabled, cfg.Host, cfg.Port, cfg.Security, cfg.Username, cfg.Password.Length > 0, cfg.FromEmail, cfg.FromName, updated);
    }

    public async Task<MailSettingsDto> UpdateAsync(UpdateMailSettingsRequest r, CancellationToken ct = default)
    {
        if (!Securities.Contains(r.Security)) throw new AppValidationException("security", $"Security must be one of {string.Join(", ", Securities)}.");
        if (r.Port is < 1 or > 65535) throw new AppValidationException("port", "Port must be between 1 and 65535.");
        var values = new Dictionary<string, (string Value, string Desc)>
        {
            ["Smtp.Enabled"] = (r.Enabled ? "true" : "false", "Send emails via SMTP (false = log only)."),
            ["Smtp.Host"] = (r.Host.Trim(), "SMTP server host."),
            ["Smtp.Port"] = (r.Port.ToString(), "SMTP port (25, 465, 587)."),
            ["Smtp.Security"] = (r.Security, "None | StartTls | StartTlsWhenAvailable | Ssl."),
            ["Smtp.Username"] = (r.Username.Trim(), "SMTP login user."),
            ["Smtp.FromEmail"] = (r.FromEmail.Trim(), "From address for outgoing mail."),
            ["Smtp.FromName"] = (r.FromName.Trim(), "Display name for outgoing mail."),
        };
        if (!string.IsNullOrEmpty(r.Password)) values["Smtp.Password"] = (_protector.Protect(r.Password), "SMTP password (encrypted at rest).");
        foreach (var (key, (value, desc)) in values)
        {
            var row = await _db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (row == null) { row = new SystemSetting { Key = key, IsPublic = false, Description = desc }; _db.SystemSettings.Add(row); }
            row.Value = value; row.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin.UpdateMailSettings", "SystemSetting", "Smtp", new { r.Host, r.Port, r.Security, r.Username, r.FromEmail, PasswordChanged = !string.IsNullOrEmpty(r.Password) }, true, ct);
        return await GetAsync(ct);
    }
}
