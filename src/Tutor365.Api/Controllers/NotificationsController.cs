using Microsoft.AspNetCore.Mvc;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Services;

namespace Tutor365.Api.Controllers;

/// <summary>In-app notifications for the current user.</summary>
public class NotificationsController : ApiControllerBase
{
    private readonly INotificationService _notifications;
    public NotificationsController(INotificationService notifications) => _notifications = notifications;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<NotificationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PagingQuery paging, [FromQuery] bool unreadOnly = false, CancellationToken ct = default)
        => Ok(await _notifications.GetMineAsync(paging, unreadOnly, ct));

    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnreadCount(CancellationToken ct) => Ok(await _notifications.GetUnreadCountAsync(ct));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await _notifications.MarkReadAsync(id, ct);
        return OkMessage("Marked as read.");
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await _notifications.MarkAllReadAsync(ct);
        return OkMessage("All notifications marked as read.");
    }
}
