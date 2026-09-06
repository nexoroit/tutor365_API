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

        var (subject, title, intro) = purpose switch
        {
            OtpPurpose.Registration => ($"{_app.Name}: verify your email", "Verify your email address", "Thanks for registering. Enter this code in the app to verify your email and finish setting up your account."),
            OtpPurpose.PasswordReset => ($"{_app.Name}: your password reset code", "Reset your password", "We received a request to reset your password. Enter this code in the app to choose a new password. If you didn't request this, you can ignore this email."),
            _ => ($"{_app.Name}: your verification code", "Your verification code", "Enter this code in the app to continue.")
        };
        var body = EmailTemplates.Para($"Hi {EmailTemplates.E(recipientName)},") + EmailTemplates.Para(intro) + EmailTemplates.CodeBox(code)
                 + EmailTemplates.Para($"<span style=\"color:#6B7280\">This code expires in {_auth.OtpExpiryMinutes} minutes.</span>");
        var (html, text) = EmailTemplates.Layout(_app.Name, _app.FrontendUrl, _app.SupportEmail, title, $"Your {_app.Name} code is {code}", body,
            $"Hi {recipientName},\n\n{intro}\n\nYour code: {code}\n\nThis code expires in {_auth.OtpExpiryMinutes} minutes.");

        await _email.SendAsync(new EmailMessage(email, recipientName, subject, html, text), ct);
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
