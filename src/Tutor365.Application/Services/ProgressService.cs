using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;

namespace Tutor365.Application.Services;

public record LessonAvailability(bool Available, string? Reason, StudentLessonProgress? Progress);

public interface IProgressService
{
    Task RecordSessionCompletionAsync(StudySession session, CancellationToken ct = default);
    Task<ProgressOverviewDto> GetOverviewAsync(Guid studentId, CancellationToken ct = default);
    Task<IReadOnlyList<SubjectProgressDto>> GetSubjectsAsync(Guid studentId, CancellationToken ct = default);
    Task<IReadOnlyList<TopicProgressDto>> GetTopicsAsync(Guid studentId, Guid? subjectId, CancellationToken ct = default);
    Task<IReadOnlyList<LessonProgressDto>> GetLessonsAsync(Guid studentId, Guid topicId, CancellationToken ct = default);
    Task<LessonAvailability> GetLessonAvailabilityAsync(Guid studentId, Guid lessonId, CancellationToken ct = default);
    Task<Lesson?> GetNextLessonAsync(Guid studentId, Guid? subjectId, Guid? topicId, CancellationToken ct = default);
    Task<PagedResult<MistakeDto>> GetMistakesAsync(Guid studentId, Guid? subjectId, Guid? sessionId, PagingQuery paging, CancellationToken ct = default);
    Task<PagedResult<SessionSummaryDto>> GetSessionsAsync(Guid studentId, StudySessionStatus? status, PagingQuery paging, CancellationToken ct = default);
    Task<IReadOnlyList<WeeklyPointDto>> GetWeeklyTrendAsync(Guid studentId, int weeks, CancellationToken ct = default);
    Task<int> GetPassThresholdAsync(Guid studentId, Guid subjectId, CancellationToken ct = default);
}

public class ProgressService : IProgressService
{
    private static readonly int[] DefaultIntervals = { 1, 2, 5, 10, 21, 45, 90 };
    private readonly IAppDbContext _db;
    private readonly INotificationService _notifications;

    public ProgressService(IAppDbContext db, INotificationService notifications) { _db = db; _notifications = notifications; }

    // =====================================================================
    // Recording
    // =====================================================================

    public async Task RecordSessionCompletionAsync(StudySession session, CancellationToken ct = default)
    {
        var student = await _db.Students.Include(s => s.User).FirstAsync(s => s.Id == session.StudentId, ct);
        var scorePercent = session.ScorePercent ?? 0;
        var minutes = Math.Max(1, session.ElapsedSeconds / 60);

        // ---- lesson progress ----
        if (session.LessonId.HasValue)
        {
            var lp = await _db.StudentLessonProgress.FirstOrDefaultAsync(x => x.StudentId == student.Id && x.LessonId == session.LessonId, ct);
            if (lp == null)
            {
                lp = new StudentLessonProgress { StudentId = student.Id, LessonId = session.LessonId.Value, FirstStartedAt = session.StartedAt };
                _db.StudentLessonProgress.Add(lp);
            }
            lp.Attempts = Math.Max(lp.Attempts, session.AttemptNumber);
            lp.LastScorePercent = scorePercent;
            lp.BestScorePercent = Math.Max(lp.BestScorePercent ?? 0, scorePercent);
            lp.CompletedAt = session.CompletedAt;
            lp.LastSessionId = session.Id;
            if (session.Passed == true && !lp.Passed) { lp.Passed = true; lp.PassedAt = session.CompletedAt; }
            lp.Status = lp.Passed ? LessonProgressStatus.Passed : LessonProgressStatus.Completed;
        }

        // ---- topic progress ----
        if (session.TopicId.HasValue)
            await UpdateTopicProgressAsync(student.Id, session.TopicId.Value, session, scorePercent, minutes, ct);

        // ---- subject progress ----
        await RecomputeSubjectProgressAsync(student.Id, session.SubjectId, ct);

        // ---- streak & totals ----
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (student.LastStudyDate != today)
        {
            student.CurrentStreakDays = student.LastStudyDate == today.AddDays(-1) ? student.CurrentStreakDays + 1 : 1;
            student.LongestStreakDays = Math.Max(student.LongestStreakDays, student.CurrentStreakDays);
            student.LastStudyDate = today;
        }
        student.TotalStudyMinutes += minutes;

        // ---- daily slot / plan item ----
        if (session.DailyStudySlotId.HasValue)
        {
            var slot = await _db.DailyStudySlots.FirstOrDefaultAsync(s => s.Id == session.DailyStudySlotId, ct);
            if (slot != null) { slot.Status = DailySlotStatus.Completed; slot.SessionId = session.Id; }
        }
        if (session.StudyPlanItemId.HasValue)
        {
            var item = await _db.StudyPlanItems.FirstOrDefaultAsync(i => i.Id == session.StudyPlanItemId, ct);
            if (item != null && session.Passed != false) { item.Status = StudyPlanItemStatus.Completed; item.CompletedAt = DateTime.UtcNow; item.CompletedSessionId = session.Id; }
        }

        await _db.SaveChangesAsync(ct);

        // ---- notifications ----
        var subjectName = await _db.Subjects.Where(s => s.Id == session.SubjectId).Select(s => s.Name).FirstAsync(ct);
        var lessonTitle = session.LessonId.HasValue ? await _db.Lessons.Where(l => l.Id == session.LessonId).Select(l => l.Title).FirstOrDefaultAsync(ct) : null;
        if (session.Type is StudySessionType.Assessment or StudySessionType.Mock) return; // assessment notifications are sent by AssessmentService
        var what = lessonTitle ?? (session.Type == StudySessionType.Review ? "a review session" : "a practice session");
        await _notifications.NotifyParentsOfStudentAsync(student.Id, NotificationType.ChildActivity,
            $"{student.User.FirstName} completed {subjectName}",
            $"{student.User.FirstName} finished {what} in {subjectName} and scored {Math.Round(scorePercent)}%{(session.Passed == true ? " (passed)" : session.Passed == false ? " (below target, will re-attempt)" : "")}.",
            new { studentId = student.Id, sessionId = session.Id, subjectId = session.SubjectId, scorePercent }, ct);

        var slotsToday = await _db.DailyStudySlots.Where(s => s.StudentId == student.Id && s.Date == today).ToListAsync(ct);
        if (slotsToday.Count > 0 && slotsToday.All(s => s.Status is DailySlotStatus.Completed or DailySlotStatus.Skipped))
        {
            await _notifications.NotifyAsync(student.UserId, NotificationType.StudyGoalCompleted, "Today's study goal complete!",
                $"You completed all {slotsToday.Count} study sessions planned for today. Great work, keep the streak going!", new { date = today }, ct);
            await _notifications.NotifyParentsOfStudentAsync(student.Id, NotificationType.StudyGoalCompleted,
                $"{student.User.FirstName} completed today's study goal",
                $"{student.User.FirstName} completed all {slotsToday.Count} study sessions planned for today.", new { studentId = student.Id, date = today }, ct);
        }
        if (session.Passed == false)
        {
            await _notifications.NotifyAsync(student.UserId, NotificationType.General, "Almost there",
                $"You scored {Math.Round(scorePercent)}% on {what}. Review your mistakes and try again to unlock the next lesson.", new { sessionId = session.Id }, ct);
        }
    }

    private async Task UpdateTopicProgressAsync(Guid studentId, Guid topicId, StudySession session, decimal scorePercent, int minutes, CancellationToken ct)
    {
        var tp = await _db.StudentTopicProgress.FirstOrDefaultAsync(x => x.StudentId == studentId && x.TopicId == topicId, ct);
        if (tp == null)
        {
            tp = new StudentTopicProgress { StudentId = studentId, TopicId = topicId };
            _db.StudentTopicProgress.Add(tp);
        }
        var scoreFraction = Math.Clamp(scorePercent / 100m, 0, 1);
        tp.MasteryScore = tp.QuestionsAttempted == 0 ? scoreFraction : Math.Round(tp.MasteryScore * 0.6m + scoreFraction * 0.4m, 4);
        tp.QuestionsAttempted += session.QuestionsAnswered;
        tp.QuestionsCorrect += session.QuestionsCorrect;
        tp.StudyTimeMinutes += minutes;
        tp.LastAttemptedAt = DateTime.UtcNow;
        tp.LessonsTotal = await _db.Lessons.CountAsync(l => l.SubTopic.TopicId == topicId && l.Status == ContentStatus.Published, ct);
        tp.LessonsCompleted = await _db.StudentLessonProgress.CountAsync(l => l.StudentId == studentId && l.Lesson.SubTopic.TopicId == topicId && l.Passed, ct);

        // confidence: consistency of the last 5 sessions on this topic
        var recent = await _db.StudySessions.Where(s => s.StudentId == studentId && s.TopicId == topicId && s.Status == StudySessionStatus.Completed && s.ScorePercent != null)
            .OrderByDescending(s => s.CompletedAt).Take(5).Select(s => s.ScorePercent!.Value).ToListAsync(ct);
        if (recent.Count >= 2)
        {
            var avg = recent.Average();
            var variance = recent.Average(v => (v - avg) * (v - avg));
            var sd = (decimal)Math.Sqrt((double)variance);
            tp.ConfidenceLevel = Math.Round(Math.Clamp(1 - sd / 50m, 0, 1) * Math.Clamp(avg / 100m, 0, 1), 4);
        }
        else tp.ConfidenceLevel = Math.Round(scoreFraction * 0.5m, 4);

        // spaced repetition
        var intervals = await GetIntervalsAsync(ct);
        if (session.Type == StudySessionType.Review) { tp.ReviewCount++; tp.LastReviewedAt = DateTime.UtcNow; }
        if (scorePercent >= 80) tp.ReviewStage = Math.Min(tp.ReviewStage + 1, intervals.Length - 1);
        else if (scorePercent < 60) tp.ReviewStage = Math.Max(0, tp.ReviewStage - 1);
        var days = scorePercent < 50 ? 1 : intervals[Math.Clamp(tp.ReviewStage, 0, intervals.Length - 1)];
        tp.NextReviewAt = DateTime.UtcNow.Date.AddDays(days);
        tp.DifficultyLevel = scorePercent >= 85 ? Math.Min(5, tp.DifficultyLevel + 1) : scorePercent < 50 ? Math.Max(1, tp.DifficultyLevel - 1) : tp.DifficultyLevel;

        var (needs, secure, mastered) = await GetMasteryThresholdsAsync(ct);
        tp.Status = tp.MasteryScore >= mastered ? MasteryStatus.Mastered
                  : tp.MasteryScore >= secure ? MasteryStatus.Secure
                  : tp.MasteryScore >= needs ? MasteryStatus.Developing
                  : MasteryStatus.NeedsPractice;
    }

    private async Task RecomputeSubjectProgressAsync(Guid studentId, Guid subjectId, CancellationToken ct)
    {
        var sp = await _db.StudentSubjectProgress.FirstOrDefaultAsync(x => x.StudentId == studentId && x.SubjectId == subjectId, ct);
        if (sp == null)
        {
            sp = new StudentSubjectProgress { StudentId = studentId, SubjectId = subjectId };
            _db.StudentSubjectProgress.Add(sp);
        }
        var sessions = await _db.StudySessions
            .Where(s => s.StudentId == studentId && s.SubjectId == subjectId && s.Status == StudySessionStatus.Completed)
            .Select(s => new { s.ScorePercent, s.ElapsedSeconds, s.QuestionsAnswered, s.QuestionsCorrect, s.CompletedAt })
            .ToListAsync(ct);
        var scored = sessions.Where(s => s.ScorePercent.HasValue).ToList();
        // weight recent sessions more: last 10 sessions weighted 2x
        var ordered = scored.OrderByDescending(s => s.CompletedAt).ToList();
        decimal weighted = 0, weights = 0;
        for (var i = 0; i < ordered.Count; i++) { var w = i < 10 ? 2m : 1m; weighted += ordered[i].ScorePercent!.Value * w; weights += w; }
        sp.AverageScore = weights == 0 ? 0 : Math.Round(weighted / weights, 2);
        sp.QuestionsAttempted = sessions.Sum(s => s.QuestionsAnswered);
        sp.QuestionsCorrect = sessions.Sum(s => s.QuestionsCorrect);
        sp.StudyTimeMinutes = sessions.Sum(s => s.ElapsedSeconds) / 60;
        sp.SessionsCompleted = sessions.Count;
        sp.LessonsCompleted = await _db.StudentLessonProgress.CountAsync(l => l.StudentId == studentId && l.Lesson.SubTopic.Topic.SubjectId == subjectId && l.Passed, ct);
        sp.LastStudiedAt = sessions.Max(s => s.CompletedAt);
        var topicMastery = await _db.StudentTopicProgress.Where(t => t.StudentId == studentId && t.Topic.SubjectId == subjectId && t.QuestionsAttempted > 0).Select(t => t.MasteryScore).ToListAsync(ct);
        sp.MasteryScore = topicMastery.Count == 0 ? 0 : Math.Round(topicMastery.Average(), 4);
        sp.CurrentGrade = scored.Count == 0 ? null : GradeHelper.EstimateGrade(sp.AverageScore);
        sp.TargetGrade = await _db.StudentSubjectSettings.Where(s => s.StudentId == studentId && s.SubjectId == subjectId).Select(s => (int?)s.TargetGrade).FirstOrDefaultAsync(ct) ?? 5;
    }

    // =====================================================================
    // Queries
    // =====================================================================

    public async Task<ProgressOverviewDto> GetOverviewAsync(Guid studentId, CancellationToken ct = default)
    {
        var student = await _db.Students.FirstAsync(s => s.Id == studentId, ct);
        var subjects = await GetSubjectsAsync(studentId, ct);
        var topics = await GetTopicsAsync(studentId, null, ct);
        var attempted = topics.Where(t => t.QuestionsAttempted > 0).ToList();
        var studied = subjects.Where(s => s.SessionsCompleted > 0).ToList();
        var overall = studied.Count == 0 ? 0 : Math.Round(studied.Average(s => s.AverageScore), 1);
        var mastery = studied.Count == 0 ? 0 : Math.Round(studied.Average(s => s.MasteryPercent), 1);
        return new ProgressOverviewDto(
            overall, mastery, studied.Count == 0 ? null : GradeHelper.EstimateGrade(overall), student.TargetGrade,
            subjects.Sum(s => s.QuestionsAttempted), subjects.Sum(s => s.QuestionsCorrect), student.TotalStudyMinutes,
            subjects.Sum(s => s.SessionsCompleted), subjects.Sum(s => s.LessonsCompleted), student.CurrentStreakDays, student.LongestStreakDays,
            subjects,
            attempted.OrderBy(t => t.MasteryPercent).Take(5).ToList(),
            attempted.Where(t => t.MasteryPercent >= 70).OrderByDescending(t => t.MasteryPercent).Take(5).ToList(),
            await GetWeeklyTrendAsync(studentId, 8, ct));
    }

    public async Task<IReadOnlyList<SubjectProgressDto>> GetSubjectsAsync(Guid studentId, CancellationToken ct = default)
    {
        var settings = await _db.StudentSubjectSettings.Include(s => s.Subject).Where(s => s.StudentId == studentId && s.IsEnabled).OrderBy(s => s.Subject.SortOrder).ToListAsync(ct);
        var progress = await _db.StudentSubjectProgress.Where(p => p.StudentId == studentId).ToListAsync(ct);
        var student = await _db.Students.Include(s => s.YearGroup).FirstAsync(s => s.Id == studentId, ct);
        var lessonTotals = await _db.Lessons.Where(l => l.Status == ContentStatus.Published && l.SubTopic.Topic.Qualification.ExamBoardId == student.ExamBoardId
                && (l.SubTopic.Topic.YearGroup == null || l.SubTopic.Topic.YearGroup.Number <= student.YearGroup.Number))
            .GroupBy(l => l.SubTopic.Topic.SubjectId).Select(g => new { SubjectId = g.Key, Count = g.Count() }).ToListAsync(ct);

        // trend: compare avg of last 5 sessions vs the 5 before
        var recentScores = await _db.StudySessions.Where(s => s.StudentId == studentId && s.Status == StudySessionStatus.Completed && s.ScorePercent != null)
            .OrderByDescending(s => s.CompletedAt).Take(60).Select(s => new { s.SubjectId, s.ScorePercent }).ToListAsync(ct);

        var list = new List<SubjectProgressDto>();
        foreach (var s in settings)
        {
            var p = progress.FirstOrDefault(x => x.SubjectId == s.SubjectId);
            var total = lessonTotals.FirstOrDefault(x => x.SubjectId == s.SubjectId)?.Count ?? 0;
            var scores = recentScores.Where(r => r.SubjectId == s.SubjectId).Select(r => r.ScorePercent!.Value).ToList();
            var trend = "Stable";
            if (scores.Count >= 4)
            {
                var recent = scores.Take(scores.Count / 2).Average(); var earlier = scores.Skip(scores.Count / 2).Average();
                trend = recent - earlier >= 5 ? "Improving" : earlier - recent >= 5 ? "Declining" : "Stable";
            }
            else if (scores.Count == 0) trend = "NotStarted";
            var avg = p?.AverageScore ?? 0;
            var status = p == null || p.SessionsCompleted == 0 ? "NotStarted"
                : avg >= GradeHelper.RequiredPercentForGrade(s.TargetGrade) ? "OnTarget"
                : avg >= GradeHelper.RequiredPercentForGrade(s.TargetGrade) - 15 ? "NearTarget" : "BelowTarget";
            list.Add(new SubjectProgressDto(s.SubjectId, s.Subject.Code, s.Subject.Name, s.Subject.ColourHex, avg,
                Math.Round((p?.MasteryScore ?? 0) * 100, 1), p?.QuestionsAttempted ?? 0, p?.QuestionsCorrect ?? 0, p?.StudyTimeMinutes ?? 0,
                p?.SessionsCompleted ?? 0, p?.LessonsCompleted ?? 0, total, p?.LastStudiedAt, p?.CurrentGrade, s.TargetGrade, status, trend));
        }
        return list;
    }

    public async Task<IReadOnlyList<TopicProgressDto>> GetTopicsAsync(Guid studentId, Guid? subjectId, CancellationToken ct = default)
    {
        var student = await _db.Students.Include(s => s.YearGroup).FirstAsync(s => s.Id == studentId, ct);
        var topicsQ = _db.Topics.Where(t => t.Status == ContentStatus.Published && t.Qualification.ExamBoardId == student.ExamBoardId);
        if (subjectId.HasValue) topicsQ = topicsQ.Where(t => t.SubjectId == subjectId);
        var topics = await topicsQ.OrderBy(t => t.Subject.SortOrder).ThenBy(t => t.SortOrder)
            .Select(t => new { t.Id, t.Code, t.Name, t.SubjectId, SubjectName = t.Subject.Name,
                LessonsTotal = t.SubTopics.SelectMany(s => s.Lessons).Count(l => l.Status == ContentStatus.Published) }).ToListAsync(ct);
        var progress = await _db.StudentTopicProgress.Where(p => p.StudentId == studentId).ToListAsync(ct);
        var passedByTopic = await _db.StudentLessonProgress.Where(l => l.StudentId == studentId && l.Passed)
            .GroupBy(l => l.Lesson.SubTopic.TopicId).Select(g => new { TopicId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var now = DateTime.UtcNow;
        return topics.Select(t =>
        {
            var p = progress.FirstOrDefault(x => x.TopicId == t.Id);
            var passedCount = passedByTopic.FirstOrDefault(x => x.TopicId == t.Id)?.Count ?? 0;
            return new TopicProgressDto(t.Id, t.Code, t.Name, t.SubjectId, t.SubjectName, Math.Round((p?.MasteryScore ?? 0) * 100, 1),
                (p?.Status ?? MasteryStatus.NotStarted).ToString(), p?.QuestionsAttempted ?? 0, p?.QuestionsCorrect ?? 0,
                passedCount, t.LessonsTotal, p?.LastAttemptedAt, p?.NextReviewAt,
                p?.NextReviewAt != null && p.NextReviewAt <= now, p?.ReviewCount ?? 0, Math.Round((p?.ConfidenceLevel ?? 0) * 100, 1),
                t.LessonsTotal > 0 && passedCount >= t.LessonsTotal && p?.AssessmentPassedAt == null, p?.LastAssessmentPercent, p?.AssessmentAttempts ?? 0, p?.AssessmentPassedAt);
        }).ToList();
    }

    public async Task<IReadOnlyList<LessonProgressDto>> GetLessonsAsync(Guid studentId, Guid topicId, CancellationToken ct = default)
    {
        var settingMax = await _db.StudentSubjectSettings.Where(s => s.StudentId == studentId && s.Subject.Id == _db.Topics.Where(t => t.Id == topicId).Select(t => t.SubjectId).First())
            .Select(s => (int?)s.MaxAttemptsBeforeMoveOn).FirstOrDefaultAsync(ct) ?? 3;
        var lessons = await _db.Lessons.Where(l => l.SubTopic.TopicId == topicId && l.Status == ContentStatus.Published)
            .OrderBy(l => l.SubTopic.SortOrder).ThenBy(l => l.SortOrder)
            .Select(l => new { l.Id, l.Title, l.SubTopicId, SubTopicName = l.SubTopic.Name, l.SortOrder, l.EstimatedMinutes, l.Difficulty, l.Tier }).ToListAsync(ct);
        var progress = await _db.StudentLessonProgress.Where(p => p.StudentId == studentId && p.Lesson.SubTopic.TopicId == topicId).ToListAsync(ct);
        var active = await _db.StudySessions.Where(s => s.StudentId == studentId && (s.Status == StudySessionStatus.Active || s.Status == StudySessionStatus.Paused) && s.LessonId != null)
            .Select(s => new { s.LessonId, s.Id }).ToListAsync(ct);

        var result = new List<LessonProgressDto>();
        var previousCleared = true;
        var order = 0;
        foreach (var l in lessons)
        {
            order++;
            var p = progress.FirstOrDefault(x => x.LessonId == l.Id);
            var cleared = p != null && (p.Passed || (settingMax > 0 && p.Attempts >= settingMax));
            var activeId = active.FirstOrDefault(a => a.LessonId == l.Id)?.Id;
            string status;
            if (!previousCleared && (p == null || p.Attempts == 0)) status = "Locked";
            else if (p == null) status = "Available";
            else if (p.Passed) status = "Passed";
            else if (activeId != null) status = "InProgress";
            else if (p.Attempts > 0) status = "Completed";
            else status = "Available";
            if (activeId != null && status != "Passed") status = "InProgress";
            result.Add(new LessonProgressDto(l.Id, l.Title, l.SubTopicId, l.SubTopicName, topicId, order, l.EstimatedMinutes, l.Difficulty, l.Tier.ToString(),
                status, p?.Attempts ?? 0, p?.BestScorePercent, p?.LastScorePercent, p?.Passed ?? false, p?.CompletedAt, activeId));
            previousCleared = cleared;
        }
        return result;
    }

    public async Task<LessonAvailability> GetLessonAvailabilityAsync(Guid studentId, Guid lessonId, CancellationToken ct = default)
    {
        var lesson = await _db.Lessons.Include(l => l.SubTopic).FirstOrDefaultAsync(l => l.Id == lessonId, ct);
        if (lesson == null) return new LessonAvailability(false, "Lesson not found.", null);
        var list = await GetLessonsAsync(studentId, lesson.SubTopic.TopicId, ct);
        var entry = list.FirstOrDefault(x => x.LessonId == lessonId);
        var progress = await _db.StudentLessonProgress.FirstOrDefaultAsync(p => p.StudentId == studentId && p.LessonId == lessonId, ct);
        if (entry == null) return new LessonAvailability(false, "Lesson is not published.", progress);
        if (entry.Status == "Locked")
        {
            var prev = list.TakeWhile(x => x.LessonId != lessonId).LastOrDefault();
            return new LessonAvailability(false, $"Pass \"{prev?.Title}\" first to unlock this lesson.", progress);
        }
        return new LessonAvailability(true, null, progress);
    }

    public async Task<Lesson?> GetNextLessonAsync(Guid studentId, Guid? subjectId, Guid? topicId, CancellationToken ct = default)
    {
        var student = await _db.Students.Include(s => s.YearGroup).FirstAsync(s => s.Id == studentId, ct);
        var topicsQ = _db.Topics.Where(t => t.Status == ContentStatus.Published && t.Qualification.ExamBoardId == student.ExamBoardId
            && (t.YearGroup == null || t.YearGroup.Number <= student.YearGroup.Number) && t.SubTopics.Any(s => s.Lessons.Any(l => l.Status == ContentStatus.Published)));
        if (topicId.HasValue) topicsQ = _db.Topics.Where(t => t.Id == topicId);
        else if (subjectId.HasValue) topicsQ = topicsQ.Where(t => t.SubjectId == subjectId);
        var topicIds = await topicsQ.OrderBy(t => t.SortOrder).Select(t => t.Id).ToListAsync(ct);
        foreach (var tid in topicIds)
        {
            var lessons = await GetLessonsAsync(studentId, tid, ct);
            var next = lessons.FirstOrDefault(l => l.Status is "InProgress" or "Available" or "Completed");
            if (next != null) return await _db.Lessons.Include(l => l.SubTopic).ThenInclude(s => s.Topic).FirstAsync(l => l.Id == next.LessonId, ct);
        }
        return null;
    }

    public async Task<PagedResult<MistakeDto>> GetMistakesAsync(Guid studentId, Guid? subjectId, Guid? sessionId, PagingQuery paging, CancellationToken ct = default)
    {
        var q = _db.StudentAnswers.Where(a => a.StudentId == studentId && !a.IsCorrect && a.AttemptNumber == 1);
        if (subjectId.HasValue) q = q.Where(a => a.Session.SubjectId == subjectId);
        if (sessionId.HasValue) q = q.Where(a => a.SessionId == sessionId);
        var page = await q.OrderByDescending(a => a.AnsweredAt)
            .Select(a => new { a.Id, a.QuestionId, a.Question.QuestionType, a.Question.QuestionText, a.AnswerText, a.AnswerJson, a.Score, a.MaxScore, a.Feedback, a.MissingCriteriaJson,
                a.Question.Explanation, a.AnsweredAt, a.SessionId, SubjectName = a.Session.Lesson != null ? a.Session.Lesson.SubTopic.Topic.Subject.Name : a.Question.SubTopic.Topic.Subject.Name,
                TopicName = a.Question.SubTopic.Topic.Name, a.IsReviewed, Question = a.Question })
            .ToPagedResultAsync(paging, ct);
        var marker = new MarkingService();
        return new PagedResult<MistakeDto>
        {
            Page = page.Page, PageSize = page.PageSize, TotalCount = page.TotalCount,
            Items = page.Items.Select(a => new MistakeDto(a.Id, a.QuestionId, a.QuestionType.ToString(), a.QuestionText, a.AnswerText, ParseJson(a.AnswerJson), a.Score, a.MaxScore, a.Feedback,
                ParseList(a.MissingCriteriaJson), a.Explanation, marker.DescribeCorrectAnswer(a.Question), a.AnsweredAt, a.SessionId, a.SubjectName, a.TopicName, a.IsReviewed)).ToList()
        };
    }

    public async Task<PagedResult<SessionSummaryDto>> GetSessionsAsync(Guid studentId, StudySessionStatus? status, PagingQuery paging, CancellationToken ct = default)
    {
        var q = _db.StudySessions.Where(s => s.StudentId == studentId);
        if (status.HasValue) q = q.Where(s => s.Status == status);
        return await q.OrderByDescending(s => s.LastActivityAt ?? s.CreatedAt).Select(SessionSummaryProjection).ToPagedResultAsync(paging, ct);
    }

    /// <summary>Server-translatable projection (uses the Subject/Topic navigations on StudySession).</summary>
    public static readonly System.Linq.Expressions.Expression<Func<StudySession, SessionSummaryDto>> SessionSummaryProjection = s =>
        new SessionSummaryDto(s.Id, s.Type.ToString(), s.Status.ToString(), s.SubjectId,
            s.Subject.Name, s.Subject.ColourHex,
            s.TopicId, s.Topic != null ? s.Topic.Name : null, s.LessonId, s.Lesson != null ? s.Lesson.Title : null, s.AttemptNumber,
            s.StartedAt, s.CompletedAt, s.ElapsedSeconds, s.ProgressPercentage, s.ScorePercent, s.QuestionsAnswered, s.QuestionsCorrect, s.Passed);

    public async Task<IReadOnlyList<WeeklyPointDto>> GetWeeklyTrendAsync(Guid studentId, int weeks, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var thisWeekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var from = thisWeekStart.AddDays(-7 * (weeks - 1));
        var sessions = await _db.StudySessions.Where(s => s.StudentId == studentId && s.Status == StudySessionStatus.Completed && s.CompletedAt >= from)
            .Select(s => new { s.CompletedAt, s.ElapsedSeconds, s.ScorePercent }).ToListAsync(ct);
        var points = new List<WeeklyPointDto>();
        for (var i = 0; i < weeks; i++)
        {
            var ws = from.AddDays(7 * i); var we = ws.AddDays(7);
            var inWeek = sessions.Where(s => s.CompletedAt >= ws && s.CompletedAt < we).ToList();
            var scored = inWeek.Where(s => s.ScorePercent.HasValue).ToList();
            points.Add(new WeeklyPointDto(DateOnly.FromDateTime(ws), inWeek.Sum(s => s.ElapsedSeconds) / 60, inWeek.Count, scored.Count == 0 ? null : Math.Round(scored.Average(s => s.ScorePercent!.Value), 1)));
        }
        return points;
    }

    public async Task<int> GetPassThresholdAsync(Guid studentId, Guid subjectId, CancellationToken ct = default) =>
        await _db.StudentSubjectSettings.Where(s => s.StudentId == studentId && s.SubjectId == subjectId).Select(s => (int?)s.PassThresholdPercent).FirstOrDefaultAsync(ct)
        ?? int.Parse(await _db.SystemSettings.Where(s => s.Key == "Lesson.DefaultPassThresholdPercent").Select(s => s.Value).FirstOrDefaultAsync(ct) ?? "70");

    private async Task<int[]> GetIntervalsAsync(CancellationToken ct)
    {
        var raw = await _db.SystemSettings.Where(s => s.Key == "SpacedRepetition.IntervalsDays").Select(s => s.Value).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(raw)) return DefaultIntervals;
        var parsed = raw.Split(',').Select(p => int.TryParse(p.Trim(), out var v) ? v : -1).Where(v => v > 0).ToArray();
        return parsed.Length == 0 ? DefaultIntervals : parsed;
    }

    private async Task<(decimal Needs, decimal Secure, decimal Mastered)> GetMasteryThresholdsAsync(CancellationToken ct)
    {
        var rows = await _db.SystemSettings.Where(s => s.Key.StartsWith("Mastery.")).ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        decimal Get(string k, decimal d) => rows.TryGetValue(k, out var v) && decimal.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : d;
        return (Get("Mastery.NeedsPracticeThreshold", 0.5m), Get("Mastery.SecureThreshold", 0.7m), Get("Mastery.MasteredThreshold", 0.9m));
    }

    public static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.Clone(); } catch { return null; }
    }

    public static IReadOnlyList<string> ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); } catch { return Array.Empty<string>(); }
    }
}
