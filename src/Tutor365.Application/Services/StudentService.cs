using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Enums;

namespace Tutor365.Application.Services;

public interface IStudentService
{
    Task<StudentDashboardDto> GetDashboardAsync(Guid studentId, CancellationToken ct = default);
    Task<IReadOnlyList<StudyPlanItemDto>> GetAssignedWorkAsync(Guid studentId, bool includeCompleted, CancellationToken ct = default);
}

public class StudentService : IStudentService
{
    private readonly IAppDbContext _db;
    private readonly IProgressService _progress;
    private readonly IPlannerService _planner;
    private readonly INotificationService _notifications;
    private readonly ICurrentUser _current;

    public StudentService(IAppDbContext db, IProgressService progress, IPlannerService planner, INotificationService notifications, ICurrentUser current)
    {
        _db = db; _progress = progress; _planner = planner; _notifications = notifications; _current = current;
    }

    public async Task<StudentDashboardDto> GetDashboardAsync(Guid studentId, CancellationToken ct = default)
    {
        var student = await _db.Students.Include(s => s.User).Include(s => s.YearGroup).Include(s => s.Schedule).FirstAsync(s => s.Id == studentId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hour = DateTime.UtcNow.Hour; // UK-centric; client can localise
        var greeting = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";

        var subjects = await _progress.GetSubjectsAsync(studentId, ct);
        var topics = await _progress.GetTopicsAsync(studentId, null, ct);
        var plan = await _planner.GetPlanAsync(studentId, today, ct);
        var recs = await _planner.GetRecommendationsAsync(studentId, 1, ct);
        var recent = (await _progress.GetSessionsAsync(studentId, StudySessionStatus.Completed, new PagingQuery(1, 5), ct)).Items;
        var active = await _db.StudySessions.Where(s => s.StudentId == studentId && (s.Status == StudySessionStatus.Active || s.Status == StudySessionStatus.Paused))
            .OrderByDescending(s => s.LastActivityAt).Select(ProgressService.SessionSummaryProjection).FirstOrDefaultAsync(ct);
        var weekly = await _progress.GetWeeklyTrendAsync(studentId, 1, ct);
        var studied = subjects.Where(s => s.SessionsCompleted > 0).ToList();
        var overall = studied.Count == 0 ? 0 : Math.Round(studied.Average(s => s.AverageScore), 1);
        var schedule = student.Schedule;
        var activeDays = schedule == null ? 7 : Enum.GetValues<DaysOfWeek>().Count(d => d is not (DaysOfWeek.None or DaysOfWeek.Weekdays or DaysOfWeek.All) && schedule.ActiveDays.HasFlag(d));
        var goal = (schedule?.SessionsPerDay ?? 2) * (schedule?.SessionMinutes ?? 45) * activeDays;
        var assigned = await GetAssignedWorkAsync(studentId, false, ct);
        var unread = _current.UserId == student.UserId ? await _notifications.GetUnreadCountAsync(ct) : 0;

        return new StudentDashboardDto(greeting, student.User.FirstName, student.YearGroup.Name, student.TargetGrade,
            studied.Count == 0 ? null : GradeHelper.EstimateGrade(overall), overall, student.CurrentStreakDays, weekly.LastOrDefault()?.StudyMinutes ?? 0, goal,
            plan, active, recs.FirstOrDefault(), subjects, recent,
            topics.Where(t => t.QuestionsAttempted > 0).OrderBy(t => t.MasteryPercent).Take(3).ToList(), assigned, unread);
    }

    public async Task<IReadOnlyList<StudyPlanItemDto>> GetAssignedWorkAsync(Guid studentId, bool includeCompleted, CancellationToken ct = default)
    {
        var q = _db.StudyPlanItems.Where(i => i.StudyPlan.StudentId == studentId && i.StudyPlan.Status == StudyPlanStatus.Active);
        if (!includeCompleted) q = q.Where(i => i.Status != StudyPlanItemStatus.Completed && i.Status != StudyPlanItemStatus.Cancelled);
        return await q.OrderBy(i => i.DueDate).ThenByDescending(i => i.Priority).ThenBy(i => i.SortOrder)
            .Select(i => new StudyPlanItemDto(i.Id, i.StudyPlanId, i.StudyPlan.Title, i.SubjectId, i.Subject.Name, i.TopicId,
                i.TopicId != null ? _db.Topics.Where(t => t.Id == i.TopicId).Select(t => t.Name).FirstOrDefault() : null,
                i.LessonId, i.Lesson != null ? i.Lesson.Title : null, i.AssessmentId, i.Priority.ToString(), i.DueDate, i.QuestionCount, i.Notes, i.Status.ToString(), i.CompletedAt))
            .ToListAsync(ct);
    }
}
