namespace Tutor365.Application.DTOs;

public record MailSettingsDto(bool Enabled, string Host, int Port, string Security, string Username, bool HasPassword, string FromEmail, string FromName, DateTime? UpdatedAt);
public record UpdateMailSettingsRequest(bool Enabled, string Host, int Port, string Security, string Username, string? Password, string FromEmail, string FromName);
public record SendTestMailRequest(string ToEmail);
