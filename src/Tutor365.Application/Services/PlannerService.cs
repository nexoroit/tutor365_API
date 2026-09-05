using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

public interface IPlannerService
{
    Task<IReadOnlyList<RecommendationDto>> GetRecommendationsAsync(Guid studentId, int count, CancellationToken ct = default);
    Task<TodayPlanDto> GetPlanAsync(Guid studentId, DateOnly date, CancellationToken ct = default);
    Task<TodayPlanDto> RegeneratePlanAsync(Guid studentId, DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<TodayPlanDto>> GetWeekAsync(Guid studentId, DateOnly weekStart, CancellationToken ct = default);
    Task SkipSlotAsync(Guid studentId, Guid slotId, CancellationToken ct = default);
}

/// <summary>Builds each day's 45-minute study slots from the parent-set schedule and the recommendation engine.</summary>
public class PlannerService : IPlannerService
{
    private readonly IAppDbContext _db;
    private readonly IProgressService _progress;

    public PlannerService(IAppDbContext db, IProgressService progress) { _db = db; _progress = progress; }

    public async Task<IReadOnlyList<RecommendationDto>> GetRecommendationsAsync(Guid studentId, int count, CancellationToken ct = default)
    {
        var settings = await _db.StudentSubjectSettings.Include(s => s.Subject).Where(s => s.StudentId == studentId && s.IsEnabled).ToListAsync(ct);
        var subjectProgress = await _db.StudentSubjectProgress.Where(p => p.StudentId == studentId).ToListAsync(ct);
        var topics = await _progress.GetTopicsAsync(studentId, null, ct);
        var now = DateTime.UtcNow;
        var candidates = new List<(decimal Score, RecommendationDto Rec)>();

        foreach (var s in settings)
        {
            var sp = subjectProgress.FirstOrDefault(p => p.SubjectId == s.SubjectId);
            var daysSince = sp?.LastStudiedAt == null ? 7 : Math.Min(14, (now - sp.LastStudiedAt.Value).TotalDays);
            var required = GradeHelper.RequiredPercentForGrade(s.TargetGrade);
            var gap = sp == null || sp.SessionsCompleted == 0 ? 10 : Math.Max(0, required - sp.AverageScore);
            var baseScore = s.Priority * 10m + (decimal)daysSince * 3m + gap * 0.6m;

            // 1) Reviews due (spaced repetition)
            var due = topics.Where(t => t.SubjectId == s.SubjectId && t.ReviewDue && t.QuestionsAttempted > 0).OrderBy(t => t.NextReviewAt).ToList();
            foreach (var t in due.Take(2))
            {
                var overdueDays = t.NextReviewAt.HasValue ? Math.Max(0, (now - t.NextReviewAt.Value).TotalDays) : 0;
                var reason = t.MasteryPercent < 50
                    ? $"{t.TopicName} is one of your weakest topics and is due for review."
                    : $"It's time to revisit {t.TopicName} so it stays fresh (spaced review).";
                candidates.Add((baseScore + 15 + (decimal)overdueDays * 2 + (70 - Math.Min(70, t.MasteryPercent)) * 0.3m,
                    new RecommendationDto(s.SubjectId, s.Subject.Name, s.Subject.ColourHex, t.TopicId, t.TopicName, null, null, "Review", reason, 1, 30)));
            }

            // 2) Next lesson in sequence
            var next = await _progress.GetNextLessonAsync(studentId, s.SubjectId, null, ct);
            if (next != null)
            {
                var lp = await _db.StudentLessonProgress.FirstOrDefaultAsync(p => p.StudentId == studentId && p.LessonId == next.Id, ct);
                var inProgress = await _db.StudySessions.AnyAsync(x => x.StudentId == studentId && x.LessonId == next.Id && (x.Status == StudySessionStatus.Active || x.Status == StudySessionStatus.Paused), ct);
                string reason;
                if (inProgress) reason = $"Continue where you left off in {next.Title}.";
                else if (lp != null && lp.Attempts > 0 && !lp.Passed) reason = $"Re-attempt {next.Title}: you scored {Math.Round(lp.LastScorePercent ?? 0)}% last time and need {s.PassThresholdPercent}% to move on.";
                else if (sp == null || sp.SessionsCompleted == 0) reason = $"Start {s.Subject.Name} with {next.SubTopic.Topic.Name}.";
                else if (gap > 15) reason = $"{s.Subject.Name} is currently below your target grade {s.TargetGrade}. The next lesson is {next.Title}.";
                else reason = $"Next in your {s.Subject.Name} learning path: {next.Title}.";
                var bonus = inProgress ? 20 : (lp != null && !lp.Passed && lp.Attempts > 0) ? 12 : 0;
                candidates.Add((baseScore + bonus,
                    new RecommendationDto(s.SubjectId, s.Subject.Name, s.Subject.ColourHex, next.SubTopic.TopicId, next.SubTopic.Topic.Name, next.Id, next.Title, "Lesson", reason, 2, next.EstimatedMinutes)));
            }
            else if (due.Count == 0)
            {
                // Everything passed: practice weakest topic in this subject.
                var weakest = topics.Where(t => t.SubjectId == s.SubjectId && t.QuestionsAttempted > 0).OrderBy(t => t.MasteryPercent).FirstOrDefault();
                if (weakest != null)
                    candidates.Add((baseScore - 5, new RecommendationDto(s.SubjectId, s.Subject.Name, s.Subject.ColourHex, weakest.TopicId, weakest.TopicName, null, null, "Practice",
                        $"You've completed every lesson in {s.Subject.Name}. Sharpen {weakest.TopicName}, your lowest mastery topic.", 3, 30)));
            }
        }

        // Prefer distinct subjects in the top picks, then fill.
        var ordered = candidates.OrderByDescending(c => c.Score).ToList();
        var picked = new List<RecommendationDto>(); var usedSubjects = new HashSet<Guid>();
        foreach (var c in ordered) if (picked.Count < count && usedSubjects.Add(c.Rec.SubjectId)) picked.Add(c.Rec);
        foreach (var c in ordered) if (picked.Count < count && !picked.Contains(c.Rec)) picked.Add(c.Rec);
        return picked.Select((r, i) => r with { Priority = i + 1 }).ToList();
    }

    public async Task<TodayPlanDto> GetPlanAsync(Guid studentId, DateOnly date, CancellationToken ct = default)
    {
        var schedule = await _db.StudySchedules.FirstOrDefaultAsync(s => s.StudentId == studentId, ct) ?? new StudySchedule { StudentId = studentId };
        var isActiveDay = schedule.ActiveDays.HasFlag(ToFlag(date.DayOfWeek));
        var slots = await _db.DailyStudySlots.Where(s => s.StudentId == studentId && s.Date == date).OrderBy(s => s.SlotNumber).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (slots.Count == 0 && isActiveDay && schedule.AutoPlanEnabled && date >= today && date <= today.AddDays(7))
        {
            slots = await GenerateAsync(studentId, date, schedule, ct);
        }
        if (date < today)
        {
            var changed = false;
            foreach (var s in slots.Where(s => s.Status is DailySlotStatus.Scheduled or DailySlotStatus.InProgress)) { s.Status = DailySlotStatus.Missed; changed = true; }
            if (changed) await _db.SaveChangesAsync(ct);
        }
        return await ToPlanDtoAsync(date, slots, isActiveDay, ct);
    }

    public async Task<TodayPlanDto> RegeneratePlanAsync(Guid studentId, DateOnly date, CancellationToken ct = default)
    {
        var schedule = await _db.StudySchedules.FirstOrDefaultAsync(s => s.StudentId == studentId, ct) ?? new StudySchedule { StudentId = studentId };
        var existing = await _db.DailyStudySlots.Where(s => s.StudentId == studentId && s.Date == date).ToListAsync(ct);
        var keep = existing.Where(s => s.Status is DailySlotStatus.Completed or DailySlotStatus.InProgress).ToList();
        _db.DailyStudySlots.RemoveRange(existing.Except(keep));
        await _db.SaveChangesAsync(ct);
        var slots = await GenerateAsync(studentId, date, schedule, ct, keep);
        return await ToPlanDtoAsync(date, slots, true, ct);
    }

    public async Task<IReadOnlyList<TodayPlanDto>> GetWeekAsync(Guid studentId, DateOnly weekStart, CancellationToken ct = default)
    {
        var list = new List<TodayPlanDto>();
        for (var i = 0; i < 7; i++) list.Add(await GetPlanAsync(studentId, weekStart.AddDays(i), ct));
        return list;
    }

    public async Task SkipSlotAsync(Guid studentId, Guid slotId, CancellationToken ct = default)
    {
        var slot = await _db.DailyStudySlots.FirstOrDefaultAsync(s => s.Id == slotId && s.StudentId == studentId, ct) ?? throw new NotFoundException("DailyStudySlot", slotId);
        if (slot.Status == DailySlotStatus.Completed) throw new BusinessRuleException("SLOT_ALREADY_COMPLETED", "This slot is already completed.");
        slot.Status = DailySlotStatus.Skipped;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<List<DailyStudySlot>> GenerateAsync(Guid studentId, DateOnly date, StudySchedule schedule, CancellationToken ct, List<DailyStudySlot>? keep = null)
    {
        var slots = keep ?? new List<DailyStudySlot>();
        var needed = Math.Max(0, schedule.SessionsPerDay - slots.Count);
        if (needed == 0) return slots;
        var recs = await GetRecommendationsAsync(studentId, needed + slots.Count + 2, ct);
        var usedSubjects = slots.Select(s => s.SubjectId).ToHashSet();
        var usedLessons = slots.Where(s => s.LessonId.HasValue).Select(s => s.LessonId!.Value).ToHashSet();
        var chosen = new List<RecommendationDto>();
        foreach (var r in recs) if (chosen.Count < needed && !usedSubjects.Contains(r.SubjectId) && (r.LessonId == null || !usedLessons.Contains(r.LessonId.Value))) { chosen.Add(r); usedSubjects.Add(r.SubjectId); }
        foreach (var r in recs) if (chosen.Count < needed && !chosen.Contains(r)) chosen.Add(r);

        var slotNo = slots.Count == 0 ? 1 : slots.Max(s => s.SlotNumber) + 1;
        foreach (var r in chosen)
        {
            var slot = new DailyStudySlot
            {
                StudentId = studentId, Date = date, SlotNumber = slotNo++, DurationMinutes = schedule.SessionMinutes,
                SubjectId = r.SubjectId, TopicId = r.TopicId, LessonId = r.LessonId,
                SessionType = Enum.TryParse<StudySessionType>(r.SessionType, out var t) ? t : StudySessionType.Lesson,
                Reason = r.Reason, Status = DailySlotStatus.Scheduled
            };
            _db.DailyStudySlots.Add(slot);
            slots.Add(slot);
        }
        await _db.SaveChangesAsync(ct);
        return slots.OrderBy(s => s.SlotNumber).ToList();
    }

    private async Task<TodayPlanDto> ToPlanDtoAsync(DateOnly date, List<DailyStudySlot> slots, bool isActiveDay, CancellationToken ct)
    {
        var subjectIds = slots.Select(s => s.SubjectId).Distinct().ToList();
        var subjects = await _db.Subjects.Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var topicIds = slots.Where(s => s.TopicId.HasValue).Select(s => s.TopicId!.Value).Distinct().ToList();
        var topics = await _db.Topics.Where(t => topicIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var lessonIds = slots.Where(s => s.LessonId.HasValue).Select(s => s.LessonId!.Value).Distinct().ToList();
        var lessons = await _db.Lessons.Where(l => lessonIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Title, ct);
        var sessionIds = slots.Where(s => s.SessionId.HasValue).Select(s => s.SessionId!.Value).ToList();
        var minutesDone = sessionIds.Count == 0 ? 0 : await _db.StudySessions.Where(s => sessionIds.Contains(s.Id)).SumAsync(s => s.ElapsedSeconds, ct) / 60;

        var dtos = slots.OrderBy(s => s.SlotNumber).Select(s => new DailySlotDto(s.Id, s.Date, s.SlotNumber, s.DurationMinutes, s.SubjectId,
            subjects.TryGetValue(s.SubjectId, out var sub) ? sub.Name : "", sub?.ColourHex, s.TopicId, s.TopicId.HasValue && topics.TryGetValue(s.TopicId.Value, out var tn) ? tn : null,
            s.LessonId, s.LessonId.HasValue && lessons.TryGetValue(s.LessonId.Value, out var ln) ? ln : null, s.SessionType.ToString(), s.Reason, s.Status.ToString(), s.SessionId)).ToList();
        return new TodayPlanDto(date, dtos.Count, dtos.Count(d => d.Status == "Completed"), dtos.Sum(d => d.DurationMinutes), minutesDone, !isActiveDay && dtos.Count == 0, dtos);
    }

    private static DaysOfWeek ToFlag(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => DaysOfWeek.Monday, DayOfWeek.Tuesday => DaysOfWeek.Tuesday, DayOfWeek.Wednesday => DaysOfWeek.Wednesday,
        DayOfWeek.Thursday => DaysOfWeek.Thursday, DayOfWeek.Friday => DaysOfWeek.Friday, DayOfWeek.Saturday => DaysOfWeek.Saturday, _ => DaysOfWeek.Sunday
    };
}
