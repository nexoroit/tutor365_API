using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Services;

namespace Tutor365.Api.Controllers;

/// <summary>Assigned work: parents (or admins) create study plans for a child; students see them on their dashboard.</summary>
[Route("api/v{version:apiVersion}")]
public class StudyPlansController : ApiControllerBase
{
    private readonly IStudyPlanService _plans;
    private readonly IReportService _reports;
    public StudyPlansController(IStudyPlanService plans, IReportService reports) { _plans = plans; _reports = reports; }

    [HttpPost("parents/me/children/{studentId:guid}/study-plans"), Authorize(Roles = Roles.ParentOrAdmin)]
    [ProducesResponseType(typeof(ApiResponse<StudyPlanDto>), 201)]
    public async Task<IActionResult> Create(Guid studentId, [FromBody] CreateStudyPlanRequest request, CancellationToken ct)
    {
        var plan = await _plans.CreateAsync(studentId, request, ct);
        return Created($"/api/v1/study-plans/{plan.Id}", plan, "Study plan created.");
    }

    [HttpGet("parents/me/children/{studentId:guid}/study-plans"), Authorize(Roles = Roles.ParentOrAdmin)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StudyPlanDto>>), 200)]
    public async Task<IActionResult> ForChild(Guid studentId, [FromQuery] bool includeInactive = false, CancellationToken ct = default) => Ok(await _plans.GetForStudentAsync(studentId, includeInactive, ct));

    [HttpGet("students/me/study-plans"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StudyPlanDto>>), 200)]
    public async Task<IActionResult> Mine([FromServices] Application.Interfaces.IAccessService access, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _plans.GetForStudentAsync(await access.GetCurrentStudentIdAsync(ct), includeInactive, ct));

    [HttpGet("study-plans/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<StudyPlanDto>), 200)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await _plans.GetAsync(id, ct));

    [HttpDelete("study-plans/{id:guid}"), Authorize(Roles = Roles.ParentOrAdmin)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) { await _plans.CancelAsync(id, ct); return OkMessage("Study plan cancelled."); }

    [HttpDelete("study-plans/{id:guid}/items/{itemId:guid}"), Authorize(Roles = Roles.ParentOrAdmin)]
    [ProducesResponseType(typeof(ApiResponse<StudyPlanDto>), 200)]
    public async Task<IActionResult> CancelItem(Guid id, Guid itemId, CancellationToken ct) => Ok(await _plans.CancelItemAsync(id, itemId, ct));

    /// <summary>Weekly report for a child (defaults to the current week, Monday to Sunday).</summary>
    [HttpGet("parents/me/children/{studentId:guid}/reports/weekly"), Authorize(Roles = Roles.ParentOrAdmin)]
    [ProducesResponseType(typeof(ApiResponse<WeeklyReportDto>), 200)]
    public async Task<IActionResult> WeeklyReport(Guid studentId, [FromQuery] DateOnly? weekStart, CancellationToken ct) => Ok(await _reports.GetWeeklyReportAsync(studentId, weekStart, ct));

    /// <summary>Report by student id for admins (and the student's own linked parent).</summary>
    [HttpGet("reports/student/{studentId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<WeeklyReportDto>), 200)]
    public async Task<IActionResult> StudentReport(Guid studentId, [FromQuery] DateOnly? weekStart, CancellationToken ct) => Ok(await _reports.GetWeeklyReportAsync(studentId, weekStart, ct));

    [HttpGet("students/me/reports/weekly"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<WeeklyReportDto>), 200)]
    public async Task<IActionResult> MyReport([FromServices] Application.Interfaces.IAccessService access, [FromQuery] DateOnly? weekStart, CancellationToken ct)
        => Ok(await _reports.GetWeeklyReportAsync(await access.GetCurrentStudentIdAsync(ct), weekStart, ct));
}
