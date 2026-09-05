using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

public interface IAssessmentService
{
    Task<AssessmentsOverviewDto> GetOverviewAsync(Guid studentId, CancellationToken ct = default);
    /// <summary>Builds a topic test paper from the question bank and stores it as an Assessment.</summary>
    Task<(Assessment Paper, List<Question> Questions)> BuildTopicTestAsync(Guid studentId, Guid topicId, CancellationToken ct = default);
    /// <summary>Builds a subject mock across the topics the student has studied.</summary>
    Task<(Assessment Paper, List<Question> Questions)> BuildMockAsync(Guid studentId, Guid subjectId, CancellationToken ct = default);
    Task RecordResultAsync(StudySession session, CancellationToken ct = default);
}

public class AssessmentService : IAssessmentService
{
    public const int TopicTestQuestions = 12;
    public const int TopicTestMinutes = 25;
    public const int MockQuestions = 24;
    public const int MockMinutes = 45;
    public const int MockMinTopics = 3;

    private readonly IAppDbContext _db;
    private readonly INotificationService _notifications;

    public AssessmentService(IAppDbContext db, INotificationService notifications) { _db = db; _notifications = notifications; }

    public async Task<AssessmentsOverviewDto> GetOverviewAsync(Guid studentId, CancellationToken ct = default)
    {
        var student = await _db.Students.Include(s => s.YearGroup).FirstAsync(s => s.Id == studentId, ct);
        var progress = await _db.StudentTopicProgress.Include(p => p.Topic).ThenInclude(t => t.Subject).Where(p => p.StudentId == studentId).ToListAsync(ct);
        var passedByTopic = await _db.StudentLessonProgress.Where(l => l.StudentId == studentId && l.Passed).GroupBy(l => l.Lesson.SubTopic.TopicId)
            .Select(g => new { TopicId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.TopicId, x => x.Count, ct);
        var lessonTotals = await _db.Lessons.Where(l => l.Status == ContentStatus.Published).GroupBy(l => l.SubTopic.TopicId)
            .Select(g => new { TopicId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.TopicId, x => x.Count, ct);

        var ready = progress
            .Where(p => lessonTotals.TryGetValue(p.TopicId, out var total) && total > 0 && passedByTopic.GetValueOrDefault(p.TopicId) >= total)
            .Select(p => new ReadyTestDto(p.TopicId, p.Topic.Name, p.Topic.SubjectId, p.Topic.Subject.Name, p.Topic.Subject.ColourHex, passedByTopic.GetValueOrDefault(p.TopicId), p.LastAssessmentPercent, p.AssessmentAttempts, p.AssessmentPassedAt != null))
            .OrderBy(r => r.Passed).ThenBy(r => r.SubjectName).ToList();

        var settings = await _db.StudentSubjectSettings.Include(s => s.Subject).Where(s => s.StudentId == studentId && s.IsEnabled).OrderBy(s => s.Subject.SortOrder).ToListAsync(ct);
        var results = await _db.AssessmentResults.Include(r => r.Assessment).ThenInclude(a => a.Subject).Include(r => r.Assessment).ThenInclude(a => a.Topic)
            .Where(r => r.StudentId == studentId).OrderByDescending(r => r.StartedAt).Take(50).ToListAsync(ct);
        var sessionIds = results.Where(r => r.SessionId.HasValue).Select(r => r.SessionId!.Value).ToList();
        var elapsed = await _db.StudySessions.Where(s => sessionIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.ElapsedSeconds, ct);

        var mocks = settings.Select(s =>
        {
            var studied = progress.Count(p => p.Topic.SubjectId == s.SubjectId && p.QuestionsAttempted > 0);
            var last = results.FirstOrDefault(r => r.Assessment.Type == AssessmentType.Mock && r.Assessment.SubjectId == s.SubjectId);
            return new MockReadinessDto(s.SubjectId, s.Subject.Name, s.Subject.ColourHex, studied, MockMinTopics, studied >= MockMinTopics, last?.Percentage, last?.EstimatedGrade);
        }).ToList();

        var resultDtos = results.Select(r => new AssessmentResultDto(r.Id, r.SessionId ?? Guid.Empty, r.Assessment.Type.ToString(), r.Assessment.Title, r.Assessment.SubjectId, r.Assessment.Subject.Name, r.Assessment.Subject.ColourHex,
            r.Assessment.TopicId, r.Assessment.Topic?.Name, r.Score, r.MaxScore, r.Percentage, r.EstimatedGrade, r.Passed, r.Assessment.PassMarkPercent, r.Assessment.TimeLimitMinutes,
            r.SessionId.HasValue && elapsed.TryGetValue(r.SessionId.Value, out var e) ? e : 0, r.StartedAt, r.CompletedAt)).ToList();

        return new AssessmentsOverviewDto(ready, mocks, resultDtos);
    }

    public async Task<(Assessment Paper, List<Question> Questions)> BuildTopicTestAsync(Guid studentId, Guid topicId, CancellationToken ct = default)
    {
        var topic = await _db.Topics.Include(t => t.Subject).FirstOrDefaultAsync(t => t.Id == topicId, ct) ?? throw new NotFoundException("Topic", topicId);
        var total = await _db.Lessons.CountAsync(l => l.SubTopic.TopicId == topicId && l.Status == ContentStatus.Published, ct);
        var passed = await _db.StudentLessonProgress.CountAsync(l => l.StudentId == studentId && l.Passed && l.Lesson.SubTopic.TopicId == topicId, ct);
        if (total == 0 || passed < total)
            throw new BusinessRuleException("TOPIC_TEST_LOCKED", $"Pass all {total} lessons in {topic.Name} to unlock the topic test ({passed} passed so far).");

        var pool = await _db.Questions.Where(q => q.Status == ContentStatus.Published && q.SubTopic.TopicId == topicId)
            .Select(q => new { q.Id, q.SubTopicId, q.Difficulty, q.IsExamStyle, q.MaxMarks }).ToListAsync(ct);
        var questions = Pick(pool.Select(p => (p.Id, p.SubTopicId, p.Difficulty, p.IsExamStyle)).ToList(), TopicTestQuestions, studentId, topicId);
        var passMark = await _db.StudentSubjectSettings.Where(s => s.StudentId == studentId && s.SubjectId == topic.SubjectId).Select(s => (int?)s.PassThresholdPercent).FirstOrDefaultAsync(ct) ?? 70;
        var paper = await StorePaperAsync(new Assessment { Title = $"{topic.Name} topic test", Type = AssessmentType.EndOfTopic, SubjectId = topic.SubjectId, TopicId = topicId, TimeLimitMinutes = TopicTestMinutes, PassMarkPercent = passMark }, questions, ct);
        return paper;
    }

    public async Task<(Assessment Paper, List<Question> Questions)> BuildMockAsync(Guid studentId, Guid subjectId, CancellationToken ct = default)
    {
        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == subjectId, ct) ?? throw new NotFoundException("Subject", subjectId);
        var studiedTopics = await _db.StudentTopicProgress.Where(p => p.StudentId == studentId && p.Topic.SubjectId == subjectId && p.QuestionsAttempted > 0).Select(p => p.TopicId).ToListAsync(ct);
        if (studiedTopics.Count < MockMinTopics)
            throw new BusinessRuleException("MOCK_LOCKED", $"Study at least {MockMinTopics} {subject.Name} topics before taking a mock ({studiedTopics.Count} so far).");
        var pool = await _db.Questions.Where(q => q.Status == ContentStatus.Published && studiedTopics.Contains(q.SubTopic.TopicId))
            .Select(q => new { q.Id, TopicId = q.SubTopic.TopicId, q.Difficulty, q.IsExamStyle }).ToListAsync(ct);
        // Group by topic for the spread, then difficulty/exam-style mix inside Pick.
        var questions = Pick(pool.Select(p => (p.Id, p.TopicId, p.Difficulty, p.IsExamStyle)).ToList(), MockQuestions, studentId, subjectId);
        var passMark = await _db.StudentSubjectSettings.Where(s => s.StudentId == studentId && s.SubjectId == subjectId).Select(s => (int?)s.PassThresholdPercent).FirstOrDefaultAsync(ct) ?? 70;
        return await StorePaperAsync(new Assessment { Title = $"{subject.Name} mock exam", Type = AssessmentType.Mock, SubjectId = subjectId, TimeLimitMinutes = MockMinutes, PassMarkPercent = Math.Min(passMark, 60) }, questions, ct);
    }

    /// <summary>Spread across groups (sub-topics or topics), about 40% exam-style, difficulty ramping easy → hard; seeded per student so re-sits differ.</summary>
    private static List<Guid> Pick(List<(Guid Id, Guid Group, int Difficulty, bool ExamStyle)> pool, int count, Guid studentId, Guid scopeId)
    {
        if (pool.Count == 0) throw new BusinessRuleException("NO_QUESTIONS_AVAILABLE", "There are no questions available for this test yet.");
        var rnd = new Random(unchecked(studentId.GetHashCode() ^ scopeId.GetHashCode() ^ Environment.TickCount));
        count = Math.Min(count, pool.Count);
        var examTarget = (int)Math.Round(count * 0.4);
        var chosen = new List<(Guid Id, Guid Group, int Difficulty, bool ExamStyle)>();
        var groups = pool.GroupBy(p => p.Group).Select(g => g.OrderBy(_ => rnd.Next()).ToList()).OrderBy(_ => rnd.Next()).ToList();
        // Round-robin across groups, preferring exam-style until the target is met.
        var idx = 0;
        while (chosen.Count < count && groups.Any(g => g.Count > 0))
        {
            var g = groups[idx % groups.Count]; idx++;
            if (g.Count == 0) continue;
            var needExam = chosen.Count(c => c.ExamStyle) < examTarget;
            var pickIdx = g.FindIndex(q => q.ExamStyle == needExam);
            if (pickIdx < 0) pickIdx = 0;
            chosen.Add(g[pickIdx]); g.RemoveAt(pickIdx);
        }
        return chosen.OrderBy(c => c.Difficulty).ThenBy(c => c.ExamStyle ? 1 : 0).Select(c => c.Id).ToList();
    }

    private async Task<(Assessment, List<Question>)> StorePaperAsync(Assessment paper, List<Guid> questionIds, CancellationToken ct)
    {
        var questions = await _db.Questions.Where(q => questionIds.Contains(q.Id)).ToListAsync(ct);
        questions = questionIds.Select(id => questions.First(q => q.Id == id)).ToList();
        paper.TotalMarks = questions.Sum(q => q.MaxMarks);
        _db.Assessments.Add(paper);
        var order = 0;
        foreach (var q in questions) _db.AssessmentQuestions.Add(new AssessmentQuestion { AssessmentId = paper.Id, QuestionId = q.Id, SortOrder = ++order, Marks = q.MaxMarks });
        await _db.SaveChangesAsync(ct);
        return (paper, questions);
    }

    public async Task RecordResultAsync(StudySession session, CancellationToken ct = default)
    {
        if (!session.AssessmentId.HasValue) return;
        var paper = await _db.Assessments.Include(a => a.Subject).Include(a => a.Topic).FirstAsync(a => a.Id == session.AssessmentId, ct);
        var pct = session.ScorePercent ?? 0;
        var passed = pct >= paper.PassMarkPercent;
        _db.AssessmentResults.Add(new AssessmentResult
        {
            AssessmentId = paper.Id, StudentId = session.StudentId, SessionId = session.Id, Score = session.CurrentScore, MaxScore = session.MaxScore, Percentage = pct,
            EstimatedGrade = GradeHelper.EstimateGrade(pct), Passed = passed, StartedAt = session.StartedAt ?? session.CreatedAt, CompletedAt = session.CompletedAt
        });
        if (paper.TopicId.HasValue)
        {
            var tp = await _db.StudentTopicProgress.FirstOrDefaultAsync(p => p.StudentId == session.StudentId && p.TopicId == paper.TopicId, ct);
            if (tp != null)
            {
                tp.LastAssessmentPercent = pct;
                tp.AssessmentAttempts++;
                if (passed && tp.AssessmentPassedAt == null) tp.AssessmentPassedAt = DateTime.UtcNow;
                if (passed && pct >= 85) tp.Status = MasteryStatus.Mastered;
            }
        }
        await _db.SaveChangesAsync(ct);
        var student = await _db.Students.Include(s => s.User).FirstAsync(s => s.Id == session.StudentId, ct);
        await _notifications.NotifyParentsOfStudentAsync(student.Id, NotificationType.SessionCompleted,
            $"{student.User.FirstName} took a {(paper.Type == AssessmentType.Mock ? "mock exam" : "topic test")}",
            $"{paper.Title}: {Math.Round(pct)}% (estimated grade {GradeHelper.EstimateGrade(pct)}){(passed ? ", passed" : ", below the pass mark")}.", new { studentId = student.Id, sessionId = session.Id }, ct);
        await _notifications.NotifyAsync(student.UserId, NotificationType.SessionCompleted, passed ? "Test passed!" : "Test complete",
            $"{paper.Title}: {Math.Round(pct)}%, estimated grade {GradeHelper.EstimateGrade(pct)}.{(passed ? " Great work." : " Review your mistakes and try again when you're ready.")}", new { sessionId = session.Id }, ct);
    }
}
