using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tutor365.Application.Common;

namespace Tutor365.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
public abstract class ApiControllerBase : ControllerBase
{
    protected OkObjectResult Ok<T>(T data, string? message = null) => base.Ok(ApiResponse<T>.Ok(data, message));
    protected OkObjectResult OkMessage(string message) => base.Ok(ApiResponse.Ok(message));
    protected CreatedResult Created<T>(string location, T data, string? message = null) => base.Created(location, ApiResponse<T>.Ok(data, message));
}

public static class Roles
{
    public const string Student = "Student";
    public const string Parent = "Parent";
    public const string Admin = "Admin";
    public const string ParentOrAdmin = "Parent,Admin";
    public const string StudentOrAdmin = "Student,Admin";
}
