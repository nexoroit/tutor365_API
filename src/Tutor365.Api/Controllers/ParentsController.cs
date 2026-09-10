using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Services;

namespace Tutor365.Api.Controllers;

/// <summary>Parent profile and management of linked children (students).</summary>
[Authorize(Roles = Roles.Parent)]
public class ParentsController : ApiControllerBase
{
    private readonly IParentService _parents;
    public ParentsController(IParentService parents) => _parents = parents;

    /// <summary>Update the parent's own profile and notification preferences.</summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(ApiResponse<ParentProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateParentProfileRequest request, CancellationToken ct)
        => Ok(await _parents.UpdateProfileAsync(request, ct));

    /// <summary>List the parent's children with headline progress.</summary>
    [HttpGet("me/children")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ChildSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChildren(CancellationToken ct) => Ok(await _parents.GetChildrenAsync(ct));

    /// <summary>Create a child account. The child logs in with their own email and password.</summary>
    [HttpPost("me/children")]
    [ProducesResponseType(typeof(ApiResponse<ChildDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateChild([FromBody] CreateChildRequest request, CancellationToken ct)
    {
        var child = await _parents.CreateChildAsync(request, ct);
        return Created($"/api/v1/parents/me/children/{child.Summary.StudentId}", child, "Child account created.");
    }

    /// <summary>Child detail including schedule and per-subject settings.</summary>
    [HttpGet("me/children/{studentId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ChildDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChild(Guid studentId, CancellationToken ct) => Ok(await _parents.GetChildAsync(studentId, ct));

    /// <summary>Update a child's name, year, exam board, target grade.</summary>
    [HttpPut("me/children/{studentId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ChildDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateChild(Guid studentId, [FromBody] UpdateChildRequest request, CancellationToken ct)
        => Ok(await _parents.UpdateChildAsync(studentId, request, ct));

    /// <summary>Set a new password for the child (parent-initiated reset).</summary>
    [HttpPost("me/children/{studentId:guid}/password")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetChildPassword(Guid studentId, [FromBody] SetChildPasswordRequest request, CancellationToken ct)
    {
        await _parents.SetChildPasswordAsync(studentId, request, ct);
        return OkMessage("Password updated.");
    }

    /// <summary>Enable or disable the child's login.</summary>
    [HttpPost("me/children/{studentId:guid}/active")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetChildActive(Guid studentId, [FromQuery] bool isActive, CancellationToken ct)
    {
        await _parents.SetChildActiveAsync(studentId, isActive, ct);
        return OkMessage(isActive ? "Account enabled." : "Account disabled.");
    }

    /// <summary>Permanently delete the child's account and all their learning history. The email can then be used again.</summary>
    [HttpDelete("me/children/{studentId:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteChild(Guid studentId, CancellationToken ct)
    {
        await _parents.DeleteChildAsync(studentId, ct);
        return OkMessage("Child account deleted.");
    }

    /// <summary>Get the child's study schedule (sessions per day, minutes, active days).</summary>
    [HttpGet("me/children/{studentId:guid}/schedule")]
    [ProducesResponseType(typeof(ApiResponse<StudyScheduleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSchedule(Guid studentId, CancellationToken ct) => Ok(await _parents.GetScheduleAsync(studentId, ct));

    /// <summary>Update the child's study schedule, e.g. 2 x 45 min or 3 x 45 min per day.</summary>
    [HttpPut("me/children/{studentId:guid}/schedule")]
    [ProducesResponseType(typeof(ApiResponse<StudyScheduleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSchedule(Guid studentId, [FromBody] UpdateStudyScheduleRequest request, CancellationToken ct)
        => Ok(await _parents.UpdateScheduleAsync(studentId, request, ct));

    /// <summary>Which in-lesson help buttons the child may use (hint, explain differently, show an example).</summary>
    [HttpGet("me/children/{studentId:guid}/help-options")]
    [ProducesResponseType(typeof(ApiResponse<HelpOptionsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHelpOptions(Guid studentId, CancellationToken ct) => Ok(await _parents.GetHelpOptionsAsync(studentId, ct));

    [HttpPut("me/children/{studentId:guid}/help-options")]
    [ProducesResponseType(typeof(ApiResponse<HelpOptionsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateHelpOptions(Guid studentId, [FromBody] UpdateHelpOptionsRequest request, CancellationToken ct)
        => Ok(await _parents.UpdateHelpOptionsAsync(studentId, request, ct), "Help options updated.");

    /// <summary>Per-subject settings: tier, target grade, pass threshold that gates the next lesson.</summary>
    [HttpGet("me/children/{studentId:guid}/subjects")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubjectSettingDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSubjectSettings(Guid studentId, CancellationToken ct) => Ok(await _parents.GetSubjectSettingsAsync(studentId, ct));

    /// <summary>Update one or more subject settings for the child.</summary>
    [HttpPut("me/children/{studentId:guid}/subjects")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubjectSettingDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSubjectSettings(Guid studentId, [FromBody] List<UpdateSubjectSettingRequest> request, CancellationToken ct)
        => Ok(await _parents.UpdateSubjectSettingsAsync(studentId, request, ct));
}
