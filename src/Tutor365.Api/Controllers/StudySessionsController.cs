using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Services;

namespace Tutor365.Api.Controllers;

/// <summary>The core learning loop: start a lesson/review, read activities, answer questions, pause/resume, complete and review results.</summary>
[Route("api/v{version:apiVersion}/study-sessions")]
public class StudySessionsController : ApiControllerBase
{
    private readonly IStudySessionService _sessions;
    public StudySessionsController(IStudySessionService sessions) => _sessions = sessions;

    /// <summary>Start (or resume) a session. Provide lessonId, or subjectId/topicId to get the next recommended lesson, or type=Review with topicId. Students only.</summary>
    [HttpPost, Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<StudySessionDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse), 422)]
    public async Task<IActionResult> Start([FromBody] StartSessionRequest request, CancellationToken ct) => Ok(await _sessions.StartAsync(request, ct));

    /// <summary>The student's current Active/Paused session, if any (for "Continue previous session?").</summary>
    [HttpGet("active"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<SessionSummaryDto>), 200)]
    public async Task<IActionResult> Active(CancellationToken ct) => Ok(await _sessions.GetActiveAsync(ct));

    /// <summary>Full session state including all activities, rendered questions and previous answers. Parents/admins may read.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<StudySessionDto>), 200)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await _sessions.GetAsync(id, ct));

    /// <summary>Submit an answer. First attempt counts for the score; later attempts give feedback only.</summary>
    [HttpPost("{id:guid}/answers"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<AnswerResultDto>), 200)]
    public async Task<IActionResult> Answer(Guid id, [FromBody] SubmitAnswerRequest request, CancellationToken ct) => Ok(await _sessions.SubmitAnswerAsync(id, request, ct));

    /// <summary>Move to the next/previous/specific activity. Direction: next | previous | goto (with activityId).</summary>
    [HttpPost("{id:guid}/navigate"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<StudySessionDto>), 200)]
    public async Task<IActionResult> Navigate(Guid id, [FromBody] NavigateRequest request, CancellationToken ct) => Ok(await _sessions.NavigateAsync(id, request, ct));

    /// <summary>Auto-save heartbeat (every 30–60s): elapsed time, current activity and free-form client state.</summary>
    [HttpPatch("{id:guid}"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<SessionProgressDto>), 200)]
    public async Task<IActionResult> Heartbeat(Guid id, [FromBody] SessionHeartbeatRequest request, CancellationToken ct) => Ok(await _sessions.HeartbeatAsync(id, request, ct));

    [HttpPost("{id:guid}/pause"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<StudySessionDto>), 200)]
    public async Task<IActionResult> Pause(Guid id, CancellationToken ct) => Ok(await _sessions.PauseAsync(id, ct));

    [HttpPost("{id:guid}/resume"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<StudySessionDto>), 200)]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct) => Ok(await _sessions.ResumeAsync(id, ct));

    /// <summary>Complete the session and get results. Fails with SESSION_INCOMPLETE if questions remain unless force=true.</summary>
    [HttpPost("{id:guid}/complete"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<SessionResultDto>), 200)]
    public async Task<IActionResult> Complete(Guid id, [FromQuery] bool force = false, CancellationToken ct = default) => Ok(await _sessions.CompleteAsync(id, force, ct));

    [HttpPost("{id:guid}/abandon"), Authorize(Roles = Roles.Student)]
    public async Task<IActionResult> Abandon(Guid id, CancellationToken ct) { await _sessions.AbandonAsync(id, ct); return OkMessage("Session abandoned."); }

    /// <summary>Results of a completed session: score, pass/fail, performance bands, mistakes, next lesson.</summary>
    [HttpGet("{id:guid}/result")]
    [ProducesResponseType(typeof(ApiResponse<SessionResultDto>), 200)]
    public async Task<IActionResult> Result(Guid id, CancellationToken ct) => Ok(await _sessions.GetResultAsync(id, ct));

    /// <summary>Hint for a question in this session (counts as hint used when the answer is submitted with hintUsed=true).</summary>
    [HttpGet("{id:guid}/questions/{questionId:guid}/hint"), Authorize(Roles = Roles.Student)]
    [ProducesResponseType(typeof(ApiResponse<string>), 200)]
    public async Task<IActionResult> Hint(Guid id, Guid questionId, CancellationToken ct) => Ok(await _sessions.GetHintAsync(id, questionId, ct));
}
