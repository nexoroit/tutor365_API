using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Enums;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

public interface IReportService
{
    Task<WeeklyReportDto> GetWeeklyReportAsync(Guid studentId, DateOnly? weekStart, CancellationToken ct = default);
    /// <summary>No access check: for background jobs.</summary>
    Task<WeeklyReportDto> BuildWeeklyReportAsync(Guid studentId, DateOnly? weekStart, CancellationToken ct = default);
}

public class ReportService : IReportService
{
    private readonly IAppDbContext _db;
    private readonly IAccessService _access;
    private readonly IProgressService _progress;
    private readonly IPlannerService _planner;

    public ReportService(IAppDbContext db, IAccessService access, IProgressService progress, IPlannerService planner)
    {
        _db = db; _access = access; _progress = progress; _planner = planner;
    }

    public async Task<WeeklyReportDto> GetWeeklyReportAsync(Guid studentId, DateOnly? weekStart, CancellationToken ct = default)
    {
        if (!await _access.CanAccessStudentAsync(studentId, ct)) throw new ForbiddenException();
        return await BuildWeeklyReportAsync(studentId, weekStart, ct);
    }

    public async Task<WeeklyReportDto> BuildWeeklyReportAsync(Guid studentId, DateOnly? weekStart, CancellationToken ct = default)
    {
        var student = await _db.Students.Include(s => s.User).Include(s => s.YearGroup).FirstAsync(s => s.Id == studentId, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = weekStart ?? today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var end = start.AddDays(6);
        var from = start.ToDateTime(TimeOnly.MinValue); var to = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var prevFrom = from.AddDays(-7);

        var sessions = await _db.StudySessions.Where(s => s.StudentId == studentId && s.Status == StudySessionStatus.Completed && s.CompletedAt >= prevFrom && s.CompletedAt < to)
            .Select(s => new { s.SubjectId, s.CompletedAt, s.ElapsedSeconds, s.ScorePercent, s.QuestionsAnswered, s.QuestionsCorrect, s.Passed, s.LessonId }).ToListAsync(ct);
        var thisWeek = sessions.Where(s => s.CompletedAt >= from).ToList();
        var lastWeek = sessions.Where(s => s.CompletedAt < from).ToList();
        var planned = await _db.DailyStudySlots.CountAsync(s => s.StudentId == studentId && s.Date >= start && s.Date <= end, ct);
        var settings = await _db.StudentSubjectSettings.Include(s => s.Subject).Where(s => s.StudentId == studentId && s.IsEnabled).OrderBy(s => s.Subject.SortOrder).ToListAsync(ct);
        var subjProgress = await _db.StudentSubjectProgress.Where(p => p.StudentId == studentId).ToListAsync(ct);

        decimal? Avg(IEnumerable<decimal?> xs) { var l = xs.Where(x => x.HasValue).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : Math.Round(l.Average(), 1); }
        var subjects = settings.Select(s =>
        {
            var tw = thisWeek.Where(x => x.SubjectId == s.SubjectId).ToList(); var lw = lastWeek.Where(x => x.SubjectId == s.SubjectId).ToList();
            var p = subjProgress.FirstOrDefault(x => x.SubjectId == s.SubjectId);
            return new SubjectWeekDto(s.SubjectId, s.Subject.Name, s.Subject.ColourHex, tw.Sum(x => x.ElapsedSeconds) / 60, tw.Count, Avg(tw.Select(x => x.ScorePercent)), Avg(lw.Select(x => x.ScorePercent)), s.TargetGrade, p?.CurrentGrade);
        }).ToList();

        var improved = subjects.Where(s => s.AverageScore.HasValue && s.PreviousWeekScore.HasValue && s.AverageScore - s.PreviousWeekScore >= 5)
            .Select(s => new SubjectChangeDto(s.SubjectId, s.SubjectName, s.PreviousWeekScore, s.AverageScore, $"Up {Math.Round(s.AverageScore!.Value - s.PreviousWeekScore!.Value)} points on last week.")).ToList();
        var attention = subjects.Where(s => (s.AverageScore.HasValue && s.AverageScore < GradeHelper.RequiredPercentForGrade(s.TargetGrade) - 10) || (s.Sessions == 0 && planned > 0))
            .Select(s => new SubjectChangeDto(s.SubjectId, s.SubjectName, s.PreviousWeekScore, s.AverageScore,
                s.Sessions == 0 ? "No sessions completed this week." : $"Averaging {Math.Round(s.AverageScore!.Value)}%, below the {GradeHelper.RequiredPercentForGrade(s.TargetGrade)}% needed for grade {s.TargetGrade}.")).ToList();

        var topics = await _progress.GetTopicsAsync(studentId, null, ct);
        var weak = topics.Where(t => t.QuestionsAttempted > 0).OrderBy(t => t.MasteryPercent).Take(5).ToList();
        var recs = await _planner.GetRecommendationsAsync(studentId, 3, ct);
        var trend = await _progress.GetWeeklyTrendAsync(studentId, 6, ct);
        var lessonsPassed = thisWeek.Count(s => s.Passed == true && s.LessonId != null);
        var avg = Avg(thisWeek.Select(s => s.ScorePercent));
        var minutes = thisWeek.Sum(s => s.ElapsedSeconds) / 60;

        var summary = thisWeek.Count == 0
            ? $"{student.User.FirstName} did not complete any study sessions this week."
            : $"{student.User.FirstName} studied for {minutes / 60}h {minutes % 60}m across {thisWeek.Count} session{(thisWeek.Count == 1 ? "" : "s")}" +
              (avg.HasValue ? $", averaging {Math.Round(avg.Value)}%" : "") + (lessonsPassed > 0 ? $" and passed {lessonsPassed} lesson{(lessonsPassed == 1 ? "" : "s")}" : "") +
              (improved.Count > 0 ? $". Improving in {string.Join(", ", improved.Select(i => i.SubjectName))}" : "") +
              (attention.Count > 0 ? $". Needs attention: {string.Join(", ", attention.Select(i => i.SubjectName))}" : "") + ".";

        return new WeeklyReportDto(studentId, student.User.FullName, student.YearGroup.Name, start, end, minutes, thisWeek.Count, planned, avg,
            thisWeek.Sum(s => s.QuestionsAnswered), thisWeek.Sum(s => s.QuestionsCorrect), lessonsPassed, student.CurrentStreakDays,
            subjects, improved, attention, weak, recs, trend, summary);
    }
}
