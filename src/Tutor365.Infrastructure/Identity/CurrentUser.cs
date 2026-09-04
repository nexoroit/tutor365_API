using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Enums;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Infrastructure.Identity;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            var id = Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal?.FindFirstValue("sub");
            return Guid.TryParse(id, out var g) ? g : null;
        }
    }

    public UserRole? Role
    {
        get
        {
            var role = Principal?.FindFirstValue(ClaimTypes.Role) ?? Principal?.FindFirstValue("role");
            return Enum.TryParse<UserRole>(role, out var r) ? r : null;
        }
    }

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email) ?? Principal?.FindFirstValue("email");

    public string? IpAddress
    {
        get
        {
            var ctx = _accessor.HttpContext;
            if (ctx == null) return null;
            var forwarded = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded)) return forwarded.Split(',')[0].Trim();
            return ctx.Connection.RemoteIpAddress?.ToString();
        }
    }

    public string? UserAgent => _accessor.HttpContext?.Request.Headers.UserAgent.FirstOrDefault();

    public bool IsInRole(UserRole role) => Role == role;

    public Guid RequireUserId() => UserId ?? throw new UnauthorizedException();
}
