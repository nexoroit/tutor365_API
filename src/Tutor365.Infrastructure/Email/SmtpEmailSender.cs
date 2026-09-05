using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Tutor365.Application.Interfaces;
using Tutor365.Application.Services;

namespace Tutor365.Infrastructure.Email;

/// <summary>Fallback SMTP settings from appsettings; the database (SystemSettings Smtp.*) takes precedence when present.</summary>
public class SmtpOptions
{
    public const string SectionName = "Smtp";
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = false;
    public bool UseStartTls { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromEmail { get; set; } = "no-reply@tutor365.local";
    public string FromName { get; set; } = "Tutor365";
}

/// <summary>Sends via SMTP using settings stored in the database (editable by admins), falling back to appsettings; logs the message when disabled.</summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly IMailSettingsService _settings;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, IMailSettingsService settings, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value; _settings = settings; _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var cfg = await _settings.GetConfigAsync(ct)
            ?? new MailConfig(_options.Enabled, _options.Host, _options.Port, _options.UseSsl ? "Ssl" : _options.UseStartTls ? "StartTls" : "StartTlsWhenAvailable", _options.Username, _options.Password, _options.FromEmail, _options.FromName);

        if (!cfg.Enabled || string.IsNullOrWhiteSpace(cfg.Host))
        {
            _logger.LogWarning("SMTP disabled. Email to {To} [{Subject}]: {Text}", message.ToEmail, message.Subject, message.TextBody ?? "(html only)");
            return;
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(cfg.FromName, cfg.FromEmail));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToEmail));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            client.Timeout = 20_000;
            var secure = cfg.Security switch
            {
                "Ssl" => SecureSocketOptions.SslOnConnect,
                "StartTls" => SecureSocketOptions.StartTls,
                "None" => SecureSocketOptions.None,
                _ => SecureSocketOptions.StartTlsWhenAvailable
            };
            await client.ConnectAsync(cfg.Host, cfg.Port, secure, ct);
            if (!string.IsNullOrEmpty(cfg.Username)) await client.AuthenticateAsync(cfg.Username, cfg.Password, ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
            _logger.LogInformation("Email sent to {To} [{Subject}] via {Host}", message.ToEmail, message.Subject, cfg.Host);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To} [{Subject}] via {Host}:{Port}", message.ToEmail, message.Subject, cfg.Host, cfg.Port);
            throw new Domain.Exceptions.BusinessRuleException("EMAIL_SEND_FAILED", "We could not send the email right now. Please try again shortly.");
        }
    }
}
