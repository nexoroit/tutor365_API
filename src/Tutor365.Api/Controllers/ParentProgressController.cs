using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Application.Services;
using Tutor365.Domain.Enums;

namespace Tutor365.Api.Controllers;

/// <summary>Parent (and admin) read-only views of a child's learning. Access is enforced against the parent-child link.</summary>
[Authorize(Roles = Roles.ParentOrAdmin)]
[Route("api/v{version:apiVersion}/parents/me/children/{studentId:guid}")]
public class ParentProgressController : ApiControllerBase
{
    private readonly IAccessService _access;
    private readonly IStudentService _students;
    private readonly IProgressService _progress;
    private readonly IPlannerService _planner;

    public ParentProgressController(IAccessService access, IStudentService students, IProgressService progress, IPlannerService planner)
    {
        _access = access; _students = students; _progress = progress; _planner = planner;
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(ApiResponse<StudentDashboardDto>), 200)]
    public async Task<IActionResult> Dashboard(Guid studentId, CancellationToken ct)
    { await _access.GetAccessibleStudentAsync(studentId, false, ct); return Ok(await _students.GetDashboardAsync(studentId, ct)); }

    [HttpGet("progress")]
    [ProducesResponseType(typeof(ApiResponse<ProgressOverviewDto>), 200)]
    public async Task<IActionResult> Progress(Guid studentId, CancellationToken ct)
    { await _access.GetAccessibleStudentAsync(studentId, false, ct); return Ok(await _progress.GetOverviewAsync(studentId, ct)); }

    [HttpGet("progress/subjects")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubjectProgressDto>>), 200)]
    public async Task<IActionResult> Subjects(Guid studentId, CancellationToken ct)
    { await _access.GetAccessibleStudentAsync(studentId, false, ct); return Ok(await _progress.GetSubjectsAsync(studentId, ct)); }

    [HttpGet("progress/topics")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TopicProgressDto>>), 200)]
    public async Task<IActionResult> Topics(Guid studentId, [FromQuery] Guid? subjectId, CancellationToken ct)
    { await _access.GetAccessibleStudentAsync(studentId, false, ct); return Ok(await _progress.GetTopicsAsync(studentId, subjectId, ct)); }

    [HttpGet("topics/{topicId:guid}/lessons")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LessonProgressDto>>), 200)]
    public async Task<IActionResult> Lessons(Guid studentId, Guid topicId, CancellationToken ct)
    { await _access.GetAccessibleStudentAsync(studentId, false, ct); return Ok(await _progress.GetLessonsAsync(studentId, topicId, ct)); }

    [HttpGet("sessions")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SessionSummaryDto>>), 200)]
    public async Task<IActionResult> Sessions(Guid studentId, [FromQuery] PagingQuery paging, [FromQuery] StudySessionStatus? status, CancellationToken ct)
    { await _access.GetAccessibleStudentAsync(studentId, false, ct); return Ok(await _progress.GetSessionsAsync(studentId, status, paging, ct)); }

    [HttpGet("mistakes")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MistakeDto>>), 200)]
    public async Task<IActionResult> Mistakes(Guid studentId, [FromQuery] PagingQuery paging, [FromQuery] Guid? subjectId, CancellationToken ct)
    { await _access.GetAccessibleStudentAsync(studentId, false, ct); return Ok(await _progress.GetMistakesAsync(studentId, subjectId, null, paging, ct)); }

    [HttpGet("today")]
    [ProducesResponseType(typeof(ApiResponse<TodayPlanDto>), 200)]
    public async Task<IActionResult> Today(Guid studentId, [FromQuery] DateOnly? date, CancellationToken ct)
    { await _access.GetAccessibleStudentAsync(studentId, false, ct); return Ok(await _planner.GetPlanAsync(studentId, date ?? DateOnly.FromDateTime(DateTime.UtcNow), ct)); }

    [HttpGet("week")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TodayPlanDto>>), 200)]
    public async Task<IActionResult> Week(Guid studentId, [FromQuery] DateOnly? weekStart, CancellationToken ct)
    {
        await _access.GetAccessibleStudentAsync(studentId, false, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await _planner.GetWeekAsync(studentId, weekStart ?? today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), ct));
    }

    [HttpGet("recommendations")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RecommendationDto>>), 200)]
    public async Task<IActionResult> Recommendations(Guid studentId, [FromQuery] int count = 5, CancellationToken ct = default)
    { await _access.GetAccessibleStudentAsync(studentId, false, ct); return Ok(await _planner.GetRecommendationsAsync(studentId, Math.Clamp(count, 1, 12), ct)); }
}
