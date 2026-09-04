using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tutor365.Application.Common;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;

namespace Tutor365.Application.Services;

public class OtpService : IOtpService
{
    private readonly IAppDbContext _db;
    private readonly IEmailSender _email;
    private readonly AuthOptions _auth;
    private readonly AppOptions _app;
    private readonly ILogger<OtpService> _logger;

    public OtpService(IAppDbContext db, IEmailSender email, IOptions<AuthOptions> auth, IOptions<AppOptions> app, ILogger<OtpService> logger)
    {
        _db = db; _email = email; _auth = auth.Value; _app = app.Value; _logger = logger;
    }

    public async Task<int> GenerateAndSendAsync(string email, string recipientName, OtpPurpose purpose, Guid? userId, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToUpperInvariant();

        // Invalidate previous outstanding codes for the same purpose.
        var existing = await _db.OtpCodes
            .Where(o => o.Email == normalized && o.Purpose == purpose && o.ConsumedAt == null)
            .ToListAsync(ct);
        foreach (var o in existing) o.ConsumedAt = DateTime.UtcNow;

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _db.OtpCodes.Add(new OtpCode
        {
            Email = normalized,
            UserId = userId,
            Purpose = purpose,
            CodeHash = Hash(code, normalized),
            ExpiresAt = DateTime.UtcNow.AddMinutes(_auth.OtpExpiryMinutes)
        });
        await _db.SaveChangesAsync(ct);

        var (subject, intro) = purpose switch
        {
            OtpPurpose.Registration => ($"{_app.Name}: verify your email", "Thanks for registering. Use the code below to verify your email address."),
            OtpPurpose.PasswordReset => ($"{_app.Name}: reset your password", "We received a request to reset your password. Use the code below to continue."),
            _ => ($"{_app.Name}: your verification code", "Use the code below to continue.")
        };

        var html = $@"<div style=""font-family:Segoe UI,Arial,sans-serif;max-width:520px;margin:auto;padding:24px"">
<h2 style=""color:#3b5bdb"">{_app.Name}</h2>
<p>Hi {System.Net.WebUtility.HtmlEncode(recipientName)},</p>
<p>{intro}</p>
<p style=""font-size:32px;letter-spacing:8px;font-weight:bold;background:#f1f3f9;padding:16px;text-align:center;border-radius:8px"">{code}</p>
<p>This code expires in {_auth.OtpExpiryMinutes} minutes. If you did not request it, you can ignore this email.</p>
<p style=""color:#888;font-size:12px"">{_app.Name} &middot; {_app.SupportEmail}</p></div>";

        await _email.SendAsync(new EmailMessage(email, recipientName, subject, html, $"Your {_app.Name} code is {code}. It expires in {_auth.OtpExpiryMinutes} minutes."), ct);
        _logger.LogInformation("OTP ({Purpose}) generated for {Email}", purpose, normalized);
        return _auth.OtpExpiryMinutes;
    }

    public async Task<bool> ValidateAndConsumeAsync(string email, OtpPurpose purpose, string code, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToUpperInvariant();
        var otp = await _db.OtpCodes
            .Where(o => o.Email == normalized && o.Purpose == purpose && o.ConsumedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (otp == null || DateTime.UtcNow > otp.ExpiresAt || otp.Attempts >= _auth.OtpMaxAttempts)
            return false;

        otp.Attempts++;
        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(otp.CodeHash), Encoding.UTF8.GetBytes(Hash(code.Trim(), normalized)));
        if (ok) otp.ConsumedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ok;
    }

    private static string Hash(string code, string salt) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{code}")));
}
