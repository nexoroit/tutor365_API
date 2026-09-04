using System.Text.Json;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;

namespace Tutor365.Infrastructure.Services;

public class AuditService : IAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly string[] Redacted = { "password", "newPassword", "currentPassword", "token", "refreshToken", "code" };

    private readonly IAppDbContext _db;
    private readonly ICurrentUser _current;

    public AuditService(IAppDbContext db, ICurrentUser current) { _db = db; _current = current; }

    public async Task LogAsync(string action, string? entityType = null, string? entityId = null, object? details = null, bool success = true, CancellationToken ct = default)
    {
        string? json = null;
        if (details != null)
        {
            json = JsonSerializer.Serialize(details, JsonOptions);
            foreach (var key in Redacted)
                json = System.Text.RegularExpressions.Regex.Replace(json, $"\"{key}\"\\s*:\\s*\"[^\"]*\"", $"\"{key}\":\"***\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (json.Length > 4000) json = json[..4000];
        }
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _current.UserId,
            UserEmail = _current.Email,
            UserRole = _current.Role,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = json,
            IpAddress = _current.IpAddress,
            Success = success
        });
        await _db.SaveChangesAsync(ct);
    }
}

public class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
