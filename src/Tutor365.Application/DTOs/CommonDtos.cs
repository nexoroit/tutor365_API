namespace Tutor365.Application.DTOs;

public record NotificationDto(Guid Id, string Type, string Title, string Message, string? DataJson, bool IsRead, DateTime CreatedAt);
public record LookupDto(Guid Id, string Code, string Name);
