using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

public interface ICurriculumService
{
    Task<IReadOnlyList<ExamBoardDto>> GetExamBoardsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<YearGroupDto>> GetYearGroupsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<QualificationDto>> GetQualificationsAsync(Guid? examBoardId, Guid? subjectId, CancellationToken ct = default);
    Task<IReadOnlyList<TopicDto>> GetTopicsAsync(Guid? subjectId, Guid? qualificationId, Guid? examBoardId, int? year, CancellationToken ct = default);
    Task<TopicDto> GetTopicAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SubTopicDto>> GetSubTopicsAsync(Guid? topicId, CancellationToken ct = default);
    Task<SubTopicDto> GetSubTopicAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<LessonSummaryDto>> GetLessonsAsync(Guid? subTopicId, Guid? topicId, Guid? subjectId, bool publishedOnly, CancellationToken ct = default);
    Task<LessonDetailDto> GetLessonAsync(Guid id, bool publishedOnly, CancellationToken ct = default);
    /// <summary>Subject tree. With includeUnpublished the draft/archived topics and sub-topics are returned too (admin view).</summary>
    Task<CurriculumTreeDto> GetTreeAsync(Guid subjectId, Guid? examBoardId, bool includeUnpublished = false, CancellationToken ct = default);
}

public class CurriculumService : ICurriculumService
{
    private readonly IAppDbContext _db;
    public CurriculumService(IAppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ExamBoardDto>> GetExamBoardsAsync(CancellationToken ct = default) =>
        await _db.ExamBoards.OrderBy(x => x.Name).Select(x => new ExamBoardDto(x.Id, x.Code, x.Name, x.Website, x.IsActive)).ToListAsync(ct);

    public async Task<IReadOnlyList<YearGroupDto>> GetYearGroupsAsync(CancellationToken ct = default) =>
        await _db.YearGroups.OrderBy(x => x.Number).Select(x => new YearGroupDto(x.Id, x.Number, x.Name, x.KeyStage.ToString())).ToListAsync(ct);

    public async Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(CancellationToken ct = default) =>
        await _db.Subjects.OrderBy(x => x.SortOrder).Select(x => new SubjectDto(x.Id, x.Code, x.Name, x.Description, x.ColourHex, x.Icon, x.SortOrder, x.IsActive)).ToListAsync(ct);

    public async Task<IReadOnlyList<QualificationDto>> GetQualificationsAsync(Guid? examBoardId, Guid? subjectId, CancellationToken ct = default)
    {
        var q = _db.Qualifications.AsQueryable();
        if (examBoardId.HasValue) q = q.Where(x => x.ExamBoardId == examBoardId);
        if (subjectId.HasValue) q = q.Where(x => x.SubjectId == subjectId);
        return await q.OrderBy(x => x.Subject.SortOrder).ThenBy(x => x.ExamBoard.Name)
            .Select(x => new QualificationDto(x.Id, x.ExamBoardId, x.ExamBoard.Name, x.SubjectId, x.Subject.Name, x.Code, x.Name, x.HasTiers, x.SpecificationUrl, x.Topics.Count))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TopicDto>> GetTopicsAsync(Guid? subjectId, Guid? qualificationId, Guid? examBoardId, int? year, CancellationToken ct = default)
    {
        var q = _db.Topics.Where(t => t.Status == ContentStatus.Published);
        if (subjectId.HasValue) q = q.Where(t => t.SubjectId == subjectId);
        if (qualificationId.HasValue) q = q.Where(t => t.QualificationId == qualificationId);
        if (examBoardId.HasValue) q = q.Where(t => t.Qualification.ExamBoardId == examBoardId);
        if (year.HasValue) q = q.Where(t => t.YearGroup != null && t.YearGroup.Number <= year);
        return await q.OrderBy(t => t.Subject.SortOrder).ThenBy(t => t.SortOrder).Select(TopicProjection).ToListAsync(ct);
    }

    public async Task<TopicDto> GetTopicAsync(Guid id, CancellationToken ct = default) =>
        await _db.Topics.Where(t => t.Id == id).Select(TopicProjection).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Topic", id);

    public async Task<IReadOnlyList<SubTopicDto>> GetSubTopicsAsync(Guid? topicId, CancellationToken ct = default)
    {
        var q = _db.SubTopics.Where(s => s.Status == ContentStatus.Published);
        if (topicId.HasValue) q = q.Where(s => s.TopicId == topicId);
        return await q.OrderBy(s => s.Topic.SortOrder).ThenBy(s => s.SortOrder).Select(SubTopicProjection).ToListAsync(ct);
    }

    public async Task<SubTopicDto> GetSubTopicAsync(Guid id, CancellationToken ct = default) =>
        await _db.SubTopics.Where(s => s.Id == id).Select(SubTopicProjection).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("SubTopic", id);

    public async Task<IReadOnlyList<LessonSummaryDto>> GetLessonsAsync(Guid? subTopicId, Guid? topicId, Guid? subjectId, bool publishedOnly, CancellationToken ct = default)
    {
        var q = _db.Lessons.AsQueryable();
        if (publishedOnly) q = q.Where(l => l.Status == ContentStatus.Published);
        if (subTopicId.HasValue) q = q.Where(l => l.SubTopicId == subTopicId);
        if (topicId.HasValue) q = q.Where(l => l.SubTopic.TopicId == topicId);
        if (subjectId.HasValue) q = q.Where(l => l.SubTopic.Topic.SubjectId == subjectId);
        var rows = await q.OrderBy(l => l.SubTopic.Topic.SortOrder).ThenBy(l => l.SubTopic.SortOrder).ThenBy(l => l.SortOrder)
            .Select(LessonProjection).ToListAsync(ct);
        return rows.Select(ToLessonSummary).ToList();
    }

    public async Task<LessonDetailDto> GetLessonAsync(Guid id, bool publishedOnly, CancellationToken ct = default)
    {
        var row = await _db.Lessons.Where(l => l.Id == id && (!publishedOnly || l.Status == ContentStatus.Published))
            .Select(LessonProjection).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Lesson", id);
        var activities = await _db.LessonActivities.Where(a => a.LessonId == id).OrderBy(a => a.SortOrder)
            .Select(a => new LessonActivityDto(a.Id, a.SortOrder, a.Type.ToString(), a.Title, a.ContentMarkdown, a.QuestionId, a.EstimatedMinutes, a.IsCheckpoint))
            .ToListAsync(ct);
        return new LessonDetailDto(ToLessonSummary(row), activities);
    }

    public async Task<CurriculumTreeDto> GetTreeAsync(Guid subjectId, Guid? examBoardId, bool includeUnpublished = false, CancellationToken ct = default)
    {
        var subject = (await GetSubjectsAsync(ct)).FirstOrDefault(s => s.Id == subjectId) ?? throw new NotFoundException("Subject", subjectId);
        var quals = await GetQualificationsAsync(examBoardId, subjectId, ct);
        var qual = quals.FirstOrDefault(q => examBoardId == null || q.ExamBoardId == examBoardId)
                   ?? throw new NotFoundException("QUALIFICATION_NOT_FOUND", "No qualification is configured for this subject and exam board yet.", true);
        var topics = await _db.Topics.Where(t => t.QualificationId == qual.Id && (includeUnpublished || t.Status == ContentStatus.Published))
            .OrderBy(t => t.SortOrder).Select(TopicProjection).ToListAsync(ct);
        var subs = await _db.SubTopics.Where(s => s.Topic.QualificationId == qual.Id && (includeUnpublished || s.Status == ContentStatus.Published))
            .OrderBy(s => s.SortOrder).Select(SubTopicProjection).ToListAsync(ct);
        var tree = topics.Select(t => new TopicTreeDto(t, subs.Where(s => s.TopicId == t.Id).ToList())).ToList();
        return new CurriculumTreeDto(subject, qual, tree);
    }

    // ---- projections ----

    private static readonly System.Linq.Expressions.Expression<Func<Topic, TopicDto>> TopicProjection = t =>
        new TopicDto(t.Id, t.QualificationId, t.SubjectId, t.Subject.Code, t.Code, t.Name, t.Description, t.SpecificationReference,
            t.SortOrder, t.ExamWeight, t.Tier.ToString(), t.YearGroup != null ? t.YearGroup.Number : null,
            t.SubTopics.Count(s => s.Status == ContentStatus.Published),
            t.SubTopics.SelectMany(s => s.Lessons).Count(l => l.Status == ContentStatus.Published),
            t.Status.ToString());

    private static readonly System.Linq.Expressions.Expression<Func<SubTopic, SubTopicDto>> SubTopicProjection = s =>
        new SubTopicDto(s.Id, s.TopicId, s.Topic.Name, s.Code, s.Name, s.Description, s.SpecificationReference, s.SortOrder, s.Tier.ToString(),
            s.Lessons.Count(l => l.Status == ContentStatus.Published),
            s.Lessons.SelectMany(l => l.Activities).Count(a => a.QuestionId != null),
            s.Mappings.Select(m => new CurriculumMappingDto(m.ExamBoardId, m.ExamBoard.Name, m.SpecificationReference, m.ReferenceBookTitle, m.ReferenceBookIsbn, m.ReferenceBookSection, m.Notes)).FirstOrDefault(),
            s.Status.ToString());

    private record LessonRow(Guid Id, Guid SubTopicId, string SubTopicName, Guid TopicId, string TopicName, Guid SubjectId, string SubjectName,
        string Title, string? Summary, string? ObjectivesJson, int EstimatedMinutes, int Difficulty, int SortOrder, Tier Tier, ContentStatus Status, int ActivityCount, int QuestionCount);

    private static readonly System.Linq.Expressions.Expression<Func<Lesson, LessonRow>> LessonProjection = l =>
        new LessonRow(l.Id, l.SubTopicId, l.SubTopic.Name, l.SubTopic.TopicId, l.SubTopic.Topic.Name, l.SubTopic.Topic.SubjectId, l.SubTopic.Topic.Subject.Name,
            l.Title, l.Summary, l.ObjectivesJson, l.EstimatedMinutes, l.Difficulty, l.SortOrder, l.Tier, l.Status,
            l.Activities.Count, l.Activities.Count(a => a.QuestionId != null));

    private static LessonSummaryDto ToLessonSummary(LessonRow r) =>
        new(r.Id, r.SubTopicId, r.SubTopicName, r.TopicId, r.TopicName, r.SubjectId, r.SubjectName, r.Title, r.Summary,
            ParseObjectives(r.ObjectivesJson), r.EstimatedMinutes, r.Difficulty, r.SortOrder, r.Tier.ToString(), r.Status.ToString(), r.ActivityCount, r.QuestionCount);

    public static IReadOnlyList<string> ParseObjectives(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch { return Array.Empty<string>(); }
    }
}
