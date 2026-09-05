using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Application.Services;
using Tutor365.Domain.Enums;

namespace Tutor365.Api.Controllers;

/// <summary>Student self-service: dashboard, today's plan, progress, results, mistakes, recommendations.</summary>
[Authorize(Roles = Roles.Student)]
public class StudentsController : ApiControllerBase
{
    private readonly IAccessService _access;
    private readonly IStudentService _students;
    private readonly IProgressService _progress;
    private readonly IPlannerService _planner;

    public StudentsController(IAccessService access, IStudentService students, IProgressService progress, IPlannerService planner)
    {
        _access = access; _students = students; _progress = progress; _planner = planner;
    }

    [HttpGet("me/dashboard")]
    [ProducesResponseType(typeof(ApiResponse<StudentDashboardDto>), 200)]
    public async Task<IActionResult> Dashboard(CancellationToken ct) => Ok(await _students.GetDashboardAsync(await _access.GetCurrentStudentIdAsync(ct), ct));

    /// <summary>Today's study slots (generated automatically from the schedule set by the parent).</summary>
    [HttpGet("me/today")]
    [ProducesResponseType(typeof(ApiResponse<TodayPlanDto>), 200)]
    public async Task<IActionResult> Today([FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(await _planner.GetPlanAsync(await _access.GetCurrentStudentIdAsync(ct), date ?? DateOnly.FromDateTime(DateTime.UtcNow), ct));

    /// <summary>Seven days of plan starting at weekStart (defaults to this Monday).</summary>
    [HttpGet("me/week")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TodayPlanDto>>), 200)]
    public async Task<IActionResult> Week([FromQuery] DateOnly? weekStart, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = weekStart ?? today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return Ok(await _planner.GetWeekAsync(await _access.GetCurrentStudentIdAsync(ct), start, ct));
    }

    [HttpPost("me/today/regenerate")]
    [ProducesResponseType(typeof(ApiResponse<TodayPlanDto>), 200)]
    public async Task<IActionResult> Regenerate([FromQuery] DateOnly? date, CancellationToken ct)
        => Ok(await _planner.RegeneratePlanAsync(await _access.GetCurrentStudentIdAsync(ct), date ?? DateOnly.FromDateTime(DateTime.UtcNow), ct));

    [HttpPost("me/today/slots/{slotId:guid}/skip")]
    public async Task<IActionResult> SkipSlot(Guid slotId, CancellationToken ct)
    {
        await _planner.SkipSlotAsync(await _access.GetCurrentStudentIdAsync(ct), slotId, ct);
        return OkMessage("Slot skipped.");
    }

    [HttpGet("me/recommendations")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RecommendationDto>>), 200)]
    public async Task<IActionResult> Recommendations([FromQuery] int count = 5, CancellationToken ct = default)
        => Ok(await _planner.GetRecommendationsAsync(await _access.GetCurrentStudentIdAsync(ct), Math.Clamp(count, 1, 12), ct));

    [HttpGet("me/progress")]
    [ProducesResponseType(typeof(ApiResponse<ProgressOverviewDto>), 200)]
    public async Task<IActionResult> Progress(CancellationToken ct) => Ok(await _progress.GetOverviewAsync(await _access.GetCurrentStudentIdAsync(ct), ct));

    [HttpGet("me/progress/subjects")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubjectProgressDto>>), 200)]
    public async Task<IActionResult> Subjects(CancellationToken ct) => Ok(await _progress.GetSubjectsAsync(await _access.GetCurrentStudentIdAsync(ct), ct));

    [HttpGet("me/progress/topics")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TopicProgressDto>>), 200)]
    public async Task<IActionResult> Topics([FromQuery] Guid? subjectId, CancellationToken ct) => Ok(await _progress.GetTopicsAsync(await _access.GetCurrentStudentIdAsync(ct), subjectId, ct));

    /// <summary>Lessons in a topic with lock/available/passed status for this student (sequential unlocking).</summary>
    [HttpGet("me/topics/{topicId:guid}/lessons")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LessonProgressDto>>), 200)]
    public async Task<IActionResult> Lessons(Guid topicId, CancellationToken ct) => Ok(await _progress.GetLessonsAsync(await _access.GetCurrentStudentIdAsync(ct), topicId, ct));

    [HttpGet("me/results")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SessionSummaryDto>>), 200)]
    public async Task<IActionResult> Results([FromQuery] PagingQuery paging, CancellationToken ct)
        => Ok(await _progress.GetSessionsAsync(await _access.GetCurrentStudentIdAsync(ct), StudySessionStatus.Completed, paging, ct));

    [HttpGet("me/sessions")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SessionSummaryDto>>), 200)]
    public async Task<IActionResult> Sessions([FromQuery] PagingQuery paging, [FromQuery] StudySessionStatus? status, CancellationToken ct)
        => Ok(await _progress.GetSessionsAsync(await _access.GetCurrentStudentIdAsync(ct), status, paging, ct));

    [HttpGet("me/mistakes")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MistakeDto>>), 200)]
    public async Task<IActionResult> Mistakes([FromQuery] PagingQuery paging, [FromQuery] Guid? subjectId, CancellationToken ct)
        => Ok(await _progress.GetMistakesAsync(await _access.GetCurrentStudentIdAsync(ct), subjectId, null, paging, ct));

    [HttpGet("me/assigned-work")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StudyPlanItemDto>>), 200)]
    public async Task<IActionResult> AssignedWork([FromQuery] bool includeCompleted = false, CancellationToken ct = default)
        => Ok(await _students.GetAssignedWorkAsync(await _access.GetCurrentStudentIdAsync(ct), includeCompleted, ct));
}
