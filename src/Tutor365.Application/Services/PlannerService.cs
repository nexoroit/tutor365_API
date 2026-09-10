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
    /// <summary>Calendar: plans for every day in [from, to]; upcoming days are generated on demand (up to the plan-ahead horizon).</summary>
    Task<IReadOnlyList<TodayPlanDto>> GetCalendarAsync(Guid studentId, DateOnly from, DateOnly to, CancellationToken ct = default);
    /// <summary>Regenerate every not-yet-started slot from the given date to the end of that week.</summary>
    Task<IReadOnlyList<TodayPlanDto>> RegenerateWeekAsync(Guid studentId, DateOnly weekStart, CancellationToken ct = default);
    /// <summary>Per-subject recommendation (review due, next lesson or practice) used by the timetable.</summary>
    Task<RecommendationDto?> GetSubjectRecommendationAsync(Guid studentId, Guid subjectId, ISet<Guid> excludeLessonIds, CancellationToken ct = default);
}

/// <summary>Builds each day's 45-minute study slots from the parent-set schedule and the recommendation engine.</summary>
public class PlannerService : IPlannerService
{
    private readonly IAppDbContext _db;
    private readonly IProgressService _progress;
    private readonly ITimetableService _timetable;

    public PlannerService(IAppDbContext db, IProgressService progress, ITimetableService timetable) { _db = db; _progress = progress; _timetable = timetable; }

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

            // 2) Topic tests that are ready (every lesson passed, test not yet passed)
            foreach (var t in topics.Where(t => t.SubjectId == s.SubjectId && t.ReadyForTest).Take(2))
                candidates.Add((baseScore + 18 + (t.AssessmentAttempts == 0 ? 4 : 0),
                    new RecommendationDto(s.SubjectId, s.Subject.Name, s.Subject.ColourHex, t.TopicId, t.TopicName, null, null, "Assessment",
                        t.AssessmentAttempts == 0 ? $"You've passed every lesson in {t.TopicName}: take the topic test." : $"Re-sit the {t.TopicName} topic test (last score {Math.Round(t.LastAssessmentPercent ?? 0)}%).", 1, AssessmentService.TopicTestMinutes)));

            // 3) Next lesson in sequence
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

        if (slots.Count == 0 && isActiveDay && schedule.AutoPlanEnabled && date >= today && date <= today.AddDays(14))
        {
            slots = await StudentLocks.RunAsync(studentId, async () =>
            {
                // Another request may have generated this day while we waited for the lock.
                var existing = await _db.DailyStudySlots.Where(s => s.StudentId == studentId && s.Date == date).OrderBy(s => s.SlotNumber).ToListAsync(ct);
                if (existing.Count > 0) return existing;
                try { return await GenerateAsync(studentId, date, schedule, ct); }
                catch (DbUpdateException)
                {
                    _db.ClearTracking();
                    return await _db.DailyStudySlots.Where(s => s.StudentId == studentId && s.Date == date).OrderBy(s => s.SlotNumber).ToListAsync(ct);
                }
            }, ct);
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
        var slots = await StudentLocks.RunAsync(studentId, () => GenerateAsync(studentId, date, schedule, ct, keep), ct);
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

    /// <summary>Generates slots for one day using the weekly subject allocation: subjects that still owe slots this week come first,
    /// avoiding repeating a subject on the same day when others are available.</summary>
    private async Task<List<DailyStudySlot>> GenerateAsync(Guid studentId, DateOnly date, StudySchedule schedule, CancellationToken ct, List<DailyStudySlot>? keep = null)
    {
        var slots = keep ?? new List<DailyStudySlot>();
        var (sessionsToday, minutesToday) = schedule.PlanFor(date.DayOfWeek);
        var needed = Math.Max(0, sessionsToday - slots.Count);
        if (needed == 0) return slots;

        var weekStart = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        var allocation = await _timetable.GetWeeklyAllocationAsync(studentId, weekStart, ct);
        var weekSlots = await _db.DailyStudySlots.Where(s => s.StudentId == studentId && s.Date >= weekStart && s.Date < weekStart.AddDays(7) && s.Status != DailySlotStatus.Skipped).ToListAsync(ct);
        var usedThisWeek = weekSlots.GroupBy(s => s.SubjectId).ToDictionary(g => g.Key, g => g.Count());
        var usedToday = slots.Select(s => s.SubjectId).ToHashSet();
        // Lessons already planned in any upcoming (or in-progress) slot, regardless of week, so the calendar never repeats a lesson.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var usedLessons = (await _db.DailyStudySlots.Where(s => s.StudentId == studentId && s.LessonId != null && s.Date >= today && s.Date != date
                && (s.Status == DailySlotStatus.Scheduled || s.Status == DailySlotStatus.InProgress)).Select(s => s.LessonId!.Value).ToListAsync(ct))
            .Concat(slots.Where(s => s.LessonId.HasValue).Select(s => s.LessonId!.Value)).ToHashSet();

        // Owed = allocation minus what is already planned this week; order by owed then weight.
        var queue = allocation.Subjects.Select(a => new { a.SubjectId, Owed = a.SlotsPerWeek - usedThisWeek.GetValueOrDefault(a.SubjectId), a.Weight })
            .OrderByDescending(x => x.Owed).ThenByDescending(x => x.Weight).ToList();

        var slotNo = slots.Count == 0 ? 1 : slots.Max(s => s.SlotNumber) + 1;
        var chosen = 0;
        foreach (var pass in new[] { 0, 1, 2 }) // pass 0: owed & not used today; pass 1: owed; pass 2: anything
        {
            foreach (var q in queue)
            {
                if (chosen >= needed) break;
                if (pass == 0 && (q.Owed <= 0 || usedToday.Contains(q.SubjectId))) continue;
                if (pass == 1 && q.Owed <= 0) continue;
                var rec = await GetSubjectRecommendationAsync(studentId, q.SubjectId, usedLessons, ct);
                if (rec == null) continue;
                var slot = new DailyStudySlot
                {
                    StudentId = studentId, Date = date, SlotNumber = slotNo++, DurationMinutes = minutesToday,
                    SubjectId = rec.SubjectId, TopicId = rec.TopicId, LessonId = rec.LessonId,
                    SessionType = Enum.TryParse<StudySessionType>(rec.SessionType, out var t) ? t : StudySessionType.Lesson,
                    Reason = rec.Reason, Status = DailySlotStatus.Scheduled
                };
                _db.DailyStudySlots.Add(slot); slots.Add(slot); weekSlots.Add(slot);
                usedToday.Add(q.SubjectId); if (rec.LessonId.HasValue) usedLessons.Add(rec.LessonId.Value);
                usedThisWeek[q.SubjectId] = usedThisWeek.GetValueOrDefault(q.SubjectId) + 1;
                chosen++;
            }
            queue = queue.Select(x => new { x.SubjectId, Owed = x.Owed - (usedToday.Contains(x.SubjectId) ? 1 : 0), x.Weight }).ToList();
            if (chosen >= needed) break;
        }
        await _db.SaveChangesAsync(ct);
        return slots.OrderBy(s => s.SlotNumber).ToList();
    }

    public async Task<RecommendationDto?> GetSubjectRecommendationAsync(Guid studentId, Guid subjectId, ISet<Guid> excludeLessonIds, CancellationToken ct = default)
    {
        var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Id == subjectId, ct);
        if (subject == null) return null;
        var topics = (await _progress.GetTopicsAsync(studentId, subjectId, ct)).ToList();
        var due = topics.Where(t => t.ReviewDue && t.QuestionsAttempted > 0).OrderBy(t => t.NextReviewAt).FirstOrDefault();
        var plannedReview = await _db.DailyStudySlots.AnyAsync(s => s.StudentId == studentId && s.SessionType == StudySessionType.Review && s.TopicId == (due != null ? due.TopicId : Guid.Empty)
            && s.Date >= DateOnly.FromDateTime(DateTime.UtcNow) && s.Status == DailySlotStatus.Scheduled, ct);
        if (due != null && !plannedReview)
            return new RecommendationDto(subjectId, subject.Name, subject.ColourHex, due.TopicId, due.TopicName, null, null, "Review",
                due.MasteryPercent < 50 ? $"{due.TopicName} is a weak topic and is due for review." : $"Spaced review of {due.TopicName} to keep it secure.", 1, 30);

        // Topic test once every lesson in a topic is passed (and not yet passed the test).
        var ready = topics.FirstOrDefault(t => t.ReadyForTest);
        if (ready != null)
        {
            var plannedTest = await _db.DailyStudySlots.AnyAsync(s => s.StudentId == studentId && s.SessionType == StudySessionType.Assessment && s.TopicId == ready.TopicId
                && s.Date >= DateOnly.FromDateTime(DateTime.UtcNow) && s.Status == DailySlotStatus.Scheduled, ct);
            if (!plannedTest)
                return new RecommendationDto(subjectId, subject.Name, subject.ColourHex, ready.TopicId, ready.TopicName, null, null, "Assessment",
                    ready.AssessmentAttempts == 0 ? $"You've passed every lesson in {ready.TopicName}: time for the topic test." : $"Re-sit the {ready.TopicName} topic test (last score {Math.Round(ready.LastAssessmentPercent ?? 0)}%).", 1, AssessmentService.TopicTestMinutes);
        }

        // Next lessons in sequence, skipping ones already planned this week.
        var student = await _db.Students.Include(s => s.YearGroup).FirstAsync(s => s.Id == studentId, ct);
        var topicIds = await _db.Topics.Where(t => t.SubjectId == subjectId && t.Status == ContentStatus.Published && t.Qualification.ExamBoardId == student.ExamBoardId
                && (t.YearGroup == null || t.YearGroup.Number <= student.YearGroup.Number)).OrderBy(t => t.SortOrder).Select(t => t.Id).ToListAsync(ct);
        foreach (var tid in topicIds)
        {
            var lessons = await _progress.GetLessonsAsync(studentId, tid, ct);
            var candidate = lessons.FirstOrDefault(l => l.Status != "Passed" && !excludeLessonIds.Contains(l.LessonId));
            if (candidate == null) continue;
            var topicName = await _db.Topics.Where(t => t.Id == tid).Select(t => t.Name).FirstAsync(ct);
            var reason = candidate.Status switch
            {
                "InProgress" => $"Continue {candidate.Title}.",
                "Completed" => $"Re-attempt {candidate.Title}: last score {Math.Round(candidate.LastScorePercent ?? 0)}%.",
                "Locked" => $"Next in sequence after the current lesson: {candidate.Title}.",
                _ => $"Next lesson in {topicName}: {candidate.Title}."
            };
            return new RecommendationDto(subjectId, subject.Name, subject.ColourHex, tid, topicName, candidate.LessonId, candidate.Title, "Lesson", reason, 2, candidate.EstimatedMinutes);
        }
        var weakest = topics.Where(t => t.QuestionsAttempted > 0).OrderBy(t => t.MasteryPercent).FirstOrDefault();
        return weakest == null ? null
            : new RecommendationDto(subjectId, subject.Name, subject.ColourHex, weakest.TopicId, weakest.TopicName, null, null, "Practice", $"All {subject.Name} lessons complete. Practice {weakest.TopicName}.", 3, 30);
    }

    public async Task<IReadOnlyList<TodayPlanDto>> GetCalendarAsync(Guid studentId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > 62) to = from.AddDays(62);
        var list = new List<TodayPlanDto>();
        for (var d = from; d <= to; d = d.AddDays(1)) list.Add(await GetPlanAsync(studentId, d, ct));
        return list;
    }

    public async Task<IReadOnlyList<TodayPlanDto>> RegenerateWeekAsync(Guid studentId, DateOnly weekStart, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = weekStart < today ? today : weekStart;
        var end = weekStart.AddDays(6);
        var existing = await _db.DailyStudySlots.Where(s => s.StudentId == studentId && s.Date >= start && s.Date <= end && s.Status == DailySlotStatus.Scheduled).ToListAsync(ct);
        _db.DailyStudySlots.RemoveRange(existing);
        await _db.SaveChangesAsync(ct);
        return await GetCalendarAsync(studentId, weekStart, end, ct);
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
