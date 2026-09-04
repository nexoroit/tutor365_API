using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;

namespace Tutor365.Application.Interfaces;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    UserRole? Role { get; }
    string? Email { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    bool IsInRole(UserRole role);
    Guid RequireUserId();
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public record TokenPair(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt);

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateAccessToken(User user, Guid? profileId);
    (string RawToken, string TokenHash, DateTime ExpiresAt) CreateRefreshToken();
    string HashToken(string rawToken);
}

public record EmailMessage(string ToEmail, string ToName, string Subject, string HtmlBody, string? TextBody = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public interface IAuditService
{
    Task LogAsync(string action, string? entityType = null, string? entityId = null, object? details = null, bool success = true, CancellationToken ct = default);
}

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
    DateOnly Today { get; }
}

/// <summary>Abstraction over the AI provider. The default implementation is a stub until a provider is chosen.</summary>
public interface IAiProvider
{
    bool IsEnabled { get; }
    string ProviderName { get; }
    Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct = default);
}

public record AiChatMessage(string Role, string Content);
public record AiRequest(string SystemPrompt, IReadOnlyList<AiChatMessage> Messages, int MaxTokens = 800, decimal Temperature = 0.4m);
public record AiCompletion(string Content, int InputTokens, int OutputTokens, bool IsStub = false);
