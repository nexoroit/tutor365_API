using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Enums;

namespace Tutor365.Application.Services;

public record SubjectAllocation(Guid SubjectId, string SubjectName, string? ColourHex, int SlotsPerWeek, decimal Weight, int LessonsRemaining, decimal RequiredLessonsPerWeek, int ReviewsDue, string Rationale);
public record WeeklyTimetableDto(DateOnly WeekStart, int YearNumber, DateOnly TargetDate, int WeeksRemaining, int SlotsPerWeek, int SessionMinutes, IReadOnlyList<string> ActiveDays, IReadOnlyList<SubjectAllocation> Subjects, int WeeklyMinutes = 0);

public interface ITimetableService
{
    /// <summary>How many of this week's slots each subject should get, and why.</summary>
    Task<WeeklyTimetableDto> GetWeeklyAllocationAsync(Guid studentId, DateOnly weekStart, CancellationToken ct = default);
    /// <summary>The date the student's curriculum scope should be finished by (exam start for Year 11, end of school year otherwise).</summary>
    Task<DateOnly> GetTargetDateAsync(int yearNumber, CancellationToken ct = default);
}

/// <summary>The "tutor's timetable": decides how much of each week goes to each subject for this student.
/// Inputs: parent-set schedule and subject priorities, year group (exam proximity), lessons remaining in the year's scope,
/// gap between current performance and target grade, and reviews falling due (spaced repetition).</summary>
public class TimetableService : ITimetableService
{
    private readonly IAppDbContext _db;
    private readonly IProgressService _progress;
    public TimetableService(IAppDbContext db, IProgressService progress) { _db = db; _progress = progress; }

    public async Task<DateOnly> GetTargetDateAsync(int yearNumber, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var key = yearNumber >= 11 ? "Timetable.Year11ExamStart" : "Timetable.SchoolYearEnd";
        var raw = await _db.SystemSettings.Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        // Values are "MM-dd"; roll to the next occurrence.
        var (m, d) = yearNumber >= 11 ? (5, 10) : (7, 20);
        if (!string.IsNullOrWhiteSpace(raw) && raw.Length >= 5 && int.TryParse(raw[..2], out var mm) && int.TryParse(raw[3..5], out var dd)) { m = mm; d = dd; }
        var target = new DateOnly(today.Year, m, Math.Min(d, DateTime.DaysInMonth(today.Year, m)));
        if (target <= today.AddDays(14)) target = new DateOnly(today.Year + 1, m, Math.Min(d, DateTime.DaysInMonth(today.Year + 1, m)));
        return target;
    }

    public async Task<WeeklyTimetableDto> GetWeeklyAllocationAsync(Guid studentId, DateOnly weekStart, CancellationToken ct = default)
    {
        var student = await _db.Students.Include(s => s.YearGroup).Include(s => s.Schedule).FirstAsync(s => s.Id == studentId, ct);
        var schedule = student.Schedule ?? new Domain.Entities.StudySchedule { StudentId = studentId };
        var activeDays = Enum.GetValues<DaysOfWeek>().Where(d => d is not (DaysOfWeek.None or DaysOfWeek.Weekdays or DaysOfWeek.All) && schedule.ActiveDays.HasFlag(d)).ToList();
        var slotsPerWeek = schedule.WeeklySessions;
        var target = await GetTargetDateAsync(student.YearGroup.Number, ct);
        var weeksRemaining = Math.Max(1, (int)Math.Ceiling((target.DayNumber - weekStart.DayNumber) / 7.0));

        var settings = await _db.StudentSubjectSettings.Include(s => s.Subject).Where(s => s.StudentId == studentId && s.IsEnabled).OrderBy(s => s.Subject.SortOrder).ToListAsync(ct);
        var subjectProgress = await _progress.GetSubjectsAsync(studentId, ct);
        var topics = await _progress.GetTopicsAsync(studentId, null, ct);

        var rows = new List<(StudentSubjectSettingView S, decimal Weight, int Remaining, decimal Required, int Reviews, string Why)>();
        foreach (var s in settings)
        {
            var sp = subjectProgress.FirstOrDefault(p => p.SubjectId == s.SubjectId);
            var remaining = Math.Max(0, (sp?.LessonsTotal ?? 0) - (sp?.LessonsCompleted ?? 0));
            var required = remaining / (decimal)weeksRemaining;                     // lessons per week to finish on time
            var reviews = topics.Count(t => t.SubjectId == s.SubjectId && t.ReviewDue);
            var requiredPct = GradeHelper.RequiredPercentForGrade(s.TargetGrade);
            var gap = sp == null || sp.SessionsCompleted == 0 ? 10 : Math.Max(0, requiredPct - sp.AverageScore);
            var paceWeight = 1m + Math.Min(3m, required);                            // more lessons left per week → more slots
            var gapWeight = 1m + gap / 40m;                                          // 20 points below target → +50%
            var priorityWeight = s.Priority / 3m;                                    // parent priority 1–5, 3 = neutral
            var reviewWeight = 1m + Math.Min(2, reviews) * 0.25m;
            var weight = Math.Round(paceWeight * gapWeight * priorityWeight * reviewWeight, 3);
            if (remaining == 0 && reviews == 0) weight *= 0.3m;                     // finished: maintenance only
            var why = remaining == 0 ? "All lessons complete; light maintenance and reviews."
                : $"{remaining} lessons left, {required:0.0}/week needed to finish by {target:d MMM}" + (gap > 10 ? $"; {Math.Round(gap)} points below grade {s.TargetGrade} target" : "") + (reviews > 0 ? $"; {reviews} review{(reviews == 1 ? "" : "s")} due" : "") + (s.Priority != 3 ? $"; parent priority {s.Priority}/5" : "") + ".";
            rows.Add((new StudentSubjectSettingView(s.SubjectId, s.Subject.Name, s.Subject.ColourHex, s.Priority, s.TargetGrade), weight, remaining, required, reviews, why));
        }

        // Largest-remainder apportionment of slotsPerWeek by weight, guaranteeing 1 slot per subject when there are enough slots.
        var allocations = new Dictionary<Guid, int>();
        var totalWeight = rows.Sum(r => r.Weight);
        if (rows.Count > 0 && totalWeight > 0)
        {
            var guaranteed = slotsPerWeek >= rows.Count ? 1 : 0;
            var pool = slotsPerWeek - guaranteed * rows.Count;
            var exact = rows.ToDictionary(r => r.S.SubjectId, r => pool * r.Weight / totalWeight);
            foreach (var r in rows) allocations[r.S.SubjectId] = guaranteed + (int)Math.Floor(exact[r.S.SubjectId]);
            var left = slotsPerWeek - allocations.Values.Sum();
            foreach (var r in rows.OrderByDescending(r => exact[r.S.SubjectId] - Math.Floor(exact[r.S.SubjectId])).ThenByDescending(r => r.Weight))
            { if (left <= 0) break; allocations[r.S.SubjectId]++; left--; }
        }

        var list = rows.Select(r => new SubjectAllocation(r.S.SubjectId, r.S.Name, r.S.Colour, allocations.GetValueOrDefault(r.S.SubjectId), r.Weight, r.Remaining, Math.Round(r.Required, 2), r.Reviews, r.Why))
            .OrderByDescending(a => a.SlotsPerWeek).ThenByDescending(a => a.Weight).ToList();
        return new WeeklyTimetableDto(weekStart, student.YearGroup.Number, target, weeksRemaining, slotsPerWeek, schedule.SessionMinutes, activeDays.Select(d => d.ToString()).ToList(), list, schedule.WeeklyMinutes);
    }

    private record StudentSubjectSettingView(Guid SubjectId, string Name, string? Colour, int Priority, int TargetGrade);
}
