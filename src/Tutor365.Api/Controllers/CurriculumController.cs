using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Services;

namespace Tutor365.Api.Controllers;

/// <summary>Read-only curriculum: exam boards, year groups, subjects, qualifications, topics, sub-topics and lessons.</summary>
[Route("api/v{version:apiVersion}")]
public class CurriculumController : ApiControllerBase
{
    private readonly ICurriculumService _curriculum;
    public CurriculumController(ICurriculumService curriculum) => _curriculum = curriculum;

    /// <summary>Exam boards (AQA, Edexcel, OCR). Public so registration forms can use it.</summary>
    [HttpGet("exam-boards"), AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ExamBoardDto>>), 200)]
    public async Task<IActionResult> ExamBoards(CancellationToken ct) => Ok(await _curriculum.GetExamBoardsAsync(ct));

    /// <summary>Year groups (9, 10, 11). Public.</summary>
    [HttpGet("year-groups"), AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<YearGroupDto>>), 200)]
    public async Task<IActionResult> YearGroups(CancellationToken ct) => Ok(await _curriculum.GetYearGroupsAsync(ct));

    /// <summary>Subjects. Public.</summary>
    [HttpGet("subjects"), AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubjectDto>>), 200)]
    public async Task<IActionResult> Subjects(CancellationToken ct) => Ok(await _curriculum.GetSubjectsAsync(ct));

    /// <summary>Full topic/sub-topic tree for a subject (defaults to the first configured exam board).</summary>
    [HttpGet("subjects/{subjectId:guid}/tree")]
    [ProducesResponseType(typeof(ApiResponse<CurriculumTreeDto>), 200)]
    public async Task<IActionResult> Tree(Guid subjectId, [FromQuery] Guid? examBoardId, CancellationToken ct) => Ok(await _curriculum.GetTreeAsync(subjectId, examBoardId, false, ct));

    [HttpGet("qualifications")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<QualificationDto>>), 200)]
    public async Task<IActionResult> Qualifications([FromQuery] Guid? examBoardId, [FromQuery] Guid? subjectId, CancellationToken ct)
        => Ok(await _curriculum.GetQualificationsAsync(examBoardId, subjectId, ct));

    /// <summary>Topics, filterable by subject, qualification, exam board and year (year returns topics taught up to and including that year).</summary>
    [HttpGet("topics")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TopicDto>>), 200)]
    public async Task<IActionResult> Topics([FromQuery] Guid? subjectId, [FromQuery] Guid? qualificationId, [FromQuery] Guid? examBoardId, [FromQuery] int? year, CancellationToken ct)
        => Ok(await _curriculum.GetTopicsAsync(subjectId, qualificationId, examBoardId, year, ct));

    [HttpGet("topics/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<TopicDto>), 200)]
    public async Task<IActionResult> Topic(Guid id, CancellationToken ct) => Ok(await _curriculum.GetTopicAsync(id, ct));

    [HttpGet("subtopics")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SubTopicDto>>), 200)]
    public async Task<IActionResult> SubTopics([FromQuery] Guid? topicId, CancellationToken ct) => Ok(await _curriculum.GetSubTopicsAsync(topicId, ct));

    [HttpGet("subtopics/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<SubTopicDto>), 200)]
    public async Task<IActionResult> SubTopic(Guid id, CancellationToken ct) => Ok(await _curriculum.GetSubTopicAsync(id, ct));

    /// <summary>Published lessons, filterable by sub-topic, topic or subject.</summary>
    [HttpGet("lessons")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LessonSummaryDto>>), 200)]
    public async Task<IActionResult> Lessons([FromQuery] Guid? subTopicId, [FromQuery] Guid? topicId, [FromQuery] Guid? subjectId, CancellationToken ct)
        => Ok(await _curriculum.GetLessonsAsync(subTopicId, topicId, subjectId, true, ct));

    /// <summary>Lesson with its ordered activities (explanations, examples, questions). Question content is fetched via study sessions.</summary>
    [HttpGet("lessons/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<LessonDetailDto>), 200)]
    public async Task<IActionResult> Lesson(Guid id, CancellationToken ct) => Ok(await _curriculum.GetLessonAsync(id, true, ct));
}
