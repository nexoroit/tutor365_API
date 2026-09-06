using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

public interface INotificationService
{
    Task NotifyAsync(Guid userId, NotificationType type, string title, string message, object? data = null, CancellationToken ct = default);
    Task NotifyParentsOfStudentAsync(Guid studentId, NotificationType type, string title, string message, object? data = null, CancellationToken ct = default);
    Task<PagedResult<NotificationDto>> GetMineAsync(PagingQuery paging, bool unreadOnly, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(CancellationToken ct = default);
    Task MarkReadAsync(Guid id, CancellationToken ct = default);
    Task MarkAllReadAsync(CancellationToken ct = default);
}

public class NotificationService : INotificationService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IAppEmailService _emails;
    public NotificationService(IAppDbContext db, ICurrentUser current, IAppEmailService emails) { _db = db; _current = current; _emails = emails; }

    /// <summary>Notification types that are also emailed (the rest stay in-app to avoid noise).</summary>
    private static readonly NotificationType[] Emailed = { NotificationType.SessionCompleted, NotificationType.PerformanceAlert, NotificationType.WeeklyReport, NotificationType.WorkAssigned };

    public async Task NotifyAsync(Guid userId, NotificationType type, string title, string message, object? data = null, CancellationToken ct = default)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId, Type = type, Title = title, Message = message,
            DataJson = data == null ? null : System.Text.Json.JsonSerializer.Serialize(data)
        });
        await _db.SaveChangesAsync(ct);
        if (type == NotificationType.WorkAssigned)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user != null && user.IsActive) await _emails.SendNotificationAsync(user, type, title, message, null, ct);
        }
    }

    public async Task NotifyParentsOfStudentAsync(Guid studentId, NotificationType type, string title, string message, object? data = null, CancellationToken ct = default)
    {
        var parents = await _db.StudentParents.Where(sp => sp.StudentId == studentId).Select(sp => new { sp.Parent.User, sp.Parent.EmailNotifications }).ToListAsync(ct);
        foreach (var p in parents)
            _db.Notifications.Add(new Notification
            {
                UserId = p.User.Id, Type = type, Title = title, Message = message,
                DataJson = data == null ? null : System.Text.Json.JsonSerializer.Serialize(data)
            });
        if (parents.Count > 0) await _db.SaveChangesAsync(ct);
        if (Emailed.Contains(type))
            foreach (var p in parents.Where(p => p.EmailNotifications && p.User.IsActive))
                await _emails.SendNotificationAsync(p.User, type, title, message, null, ct);
    }

    public async Task<PagedResult<NotificationDto>> GetMineAsync(PagingQuery paging, bool unreadOnly, CancellationToken ct = default)
    {
        var userId = _current.RequireUserId();
        var q = _db.Notifications.Where(n => n.UserId == userId);
        if (unreadOnly) q = q.Where(n => !n.IsRead);
        return await q.OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationDto(n.Id, n.Type.ToString(), n.Title, n.Message, n.DataJson, n.IsRead, n.CreatedAt))
            .ToPagedResultAsync(paging, ct);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken ct = default)
    {
        var userId = _current.RequireUserId();
        return await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        var userId = _current.RequireUserId();
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct) ?? throw new NotFoundException("Notification", id);
        n.IsRead = true; n.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var userId = _current.RequireUserId();
        var list = await _db.Notifications.Where(x => x.UserId == userId && !x.IsRead).ToListAsync(ct);
        foreach (var n in list) { n.IsRead = true; n.ReadAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync(ct);
    }
}
