using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Tutor365.Application.Interfaces;

namespace Tutor365.Infrastructure.Email;

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

/// <summary>Sends via SMTP when configured; otherwise logs the message (so OTP codes are visible in dev logs).</summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value; _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Host))
        {
            _logger.LogWarning("SMTP disabled. Email to {To} [{Subject}]: {Text}", message.ToEmail, message.Subject, message.TextBody ?? "(html only)");
            return;
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        mime.To.Add(new MailboxAddress(message.ToName, message.ToEmail));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            var secure = _options.UseSsl ? SecureSocketOptions.SslOnConnect
                       : _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(_options.Host, _options.Port, secure, ct);
            if (!string.IsNullOrEmpty(_options.Username))
                await client.AuthenticateAsync(_options.Username, _options.Password, ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
            _logger.LogInformation("Email sent to {To} [{Subject}]", message.ToEmail, message.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To} [{Subject}]", message.ToEmail, message.Subject);
            throw new Domain.Exceptions.BusinessRuleException("EMAIL_SEND_FAILED", "We could not send the email right now. Please try again shortly.");
        }
    }
}
