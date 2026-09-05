using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

public interface IStudySessionService
{
    Task<StudySessionDto> StartAsync(StartSessionRequest request, CancellationToken ct = default);
    Task<StudySessionDto> GetAsync(Guid sessionId, CancellationToken ct = default);
    Task<SessionSummaryDto?> GetActiveAsync(CancellationToken ct = default);
    Task<AnswerResultDto> SubmitAnswerAsync(Guid sessionId, SubmitAnswerRequest request, CancellationToken ct = default);
    Task<StudySessionDto> NavigateAsync(Guid sessionId, NavigateRequest request, CancellationToken ct = default);
    Task<SessionProgressDto> HeartbeatAsync(Guid sessionId, SessionHeartbeatRequest request, CancellationToken ct = default);
    Task<StudySessionDto> PauseAsync(Guid sessionId, CancellationToken ct = default);
    Task<StudySessionDto> ResumeAsync(Guid sessionId, CancellationToken ct = default);
    Task<SessionResultDto> CompleteAsync(Guid sessionId, bool force, CancellationToken ct = default);
    Task AbandonAsync(Guid sessionId, CancellationToken ct = default);
    Task<SessionResultDto> GetResultAsync(Guid sessionId, CancellationToken ct = default);
    Task<string?> GetHintAsync(Guid sessionId, Guid questionId, CancellationToken ct = default);
}

public class StudySessionService : IStudySessionService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IAccessService _access;
    private readonly IMarkingService _marking;
    private readonly IProgressService _progress;
    private readonly IAuditService _audit;

    public StudySessionService(IAppDbContext db, ICurrentUser current, IAccessService access, IMarkingService marking, IProgressService progress, IAuditService audit)
    {
        _db = db; _current = current; _access = access; _marking = marking; _progress = progress; _audit = audit;
    }

    // =====================================================================
    // Start
    // =====================================================================

    public async Task<StudySessionDto> StartAsync(StartSessionRequest request, CancellationToken ct = default)
    {
        var studentId = await _access.GetCurrentStudentIdAsync(ct);
        var type = Enum.TryParse<StudySessionType>(request.Type, true, out var t) ? t : StudySessionType.Lesson;

        // Resolve the slot first: it may carry the lesson/topic.
        DailyStudySlot? slot = null;
        if (request.DailyStudySlotId.HasValue)
        {
            slot = await _db.DailyStudySlots.FirstOrDefaultAsync(s => s.Id == request.DailyStudySlotId && s.StudentId == studentId, ct)
                ?? throw new NotFoundException("DailyStudySlot", request.DailyStudySlotId);
            if (slot.SessionId.HasValue && slot.Status == DailySlotStatus.Completed)
                throw new BusinessRuleException("SLOT_ALREADY_COMPLETED", "This study slot has already been completed.");
            type = slot.SessionType;
        }
        StudyPlanItem? planItem = null;
        if (request.StudyPlanItemId.HasValue)
        {
            planItem = await _db.StudyPlanItems.Include(i => i.StudyPlan).FirstOrDefaultAsync(i => i.Id == request.StudyPlanItemId && i.StudyPlan.StudentId == studentId, ct)
                ?? throw new NotFoundException("StudyPlanItem", request.StudyPlanItemId);
        }

        var lessonId = request.LessonId ?? slot?.LessonId ?? planItem?.LessonId;
        var topicId = request.TopicId ?? slot?.TopicId ?? planItem?.TopicId;
        var subjectId = request.SubjectId ?? slot?.SubjectId ?? planItem?.SubjectId;

        // Lesson sessions: resolve lesson (explicit, or next recommended in subject/topic).
        Lesson? lesson = null;
        if (type == StudySessionType.Lesson)
        {
            if (lessonId.HasValue)
                lesson = await _db.Lessons.Include(l => l.SubTopic).ThenInclude(s => s.Topic).Include(l => l.Activities)
                    .FirstOrDefaultAsync(l => l.Id == lessonId && l.Status == ContentStatus.Published, ct) ?? throw new NotFoundException("Lesson", lessonId);
            else
            {
                lesson = await _progress.GetNextLessonAsync(studentId, subjectId, topicId, ct)
                    ?? throw new BusinessRuleException("NO_LESSON_AVAILABLE", "There is no lesson available to study yet for this selection.");
                await _db.Lessons.Entry(lesson).Collection(l => l.Activities).LoadAsync(ct);
            }
            topicId = lesson.SubTopic.TopicId;
            subjectId = lesson.SubTopic.Topic.SubjectId;

            // Resume an in-flight session for the same lesson unless the caller forces a new one.
            var existing = await _db.StudySessions.FirstOrDefaultAsync(s => s.StudentId == studentId && s.LessonId == lesson.Id
                && (s.Status == StudySessionStatus.Active || s.Status == StudySessionStatus.Paused), ct);
            if (existing != null && !request.ForceNew)
            {
                if (existing.Status == StudySessionStatus.Paused) await ResumeInternalAsync(existing, ct);
                return await BuildSessionDtoAsync(existing.Id, ct);
            }
            if (existing != null) { existing.Status = StudySessionStatus.Abandoned; existing.UpdatedAt = DateTime.UtcNow; }

            var availability = await _progress.GetLessonAvailabilityAsync(studentId, lesson.Id, ct);
            if (!availability.Available)
            {
                // A planned slot may point at a lesson that is still locked (the previous one was not passed): fall back to the next available lesson.
                var fallback = slot != null ? await _progress.GetNextLessonAsync(studentId, lesson.SubTopic.Topic.SubjectId, null, ct) : null;
                if (fallback == null || fallback.Id == lesson.Id) throw new BusinessRuleException("LESSON_LOCKED", availability.Reason ?? "This lesson is locked.");
                lesson = fallback;
                await _db.Lessons.Entry(lesson).Collection(l => l.Activities).LoadAsync(ct);
                topicId = lesson.SubTopic.TopicId;
                slot!.LessonId = lesson.Id; slot.TopicId = topicId; slot.Reason = $"Re-attempt {lesson.Title} before moving on.";
            }
        }
        else
        {
            if (!topicId.HasValue && !lessonId.HasValue) throw new AppValidationException("topicId", "A topic is required for a review or practice session.");
            if (lessonId.HasValue && !topicId.HasValue)
                topicId = await _db.Lessons.Where(l => l.Id == lessonId).Select(l => l.SubTopic.TopicId).FirstOrDefaultAsync(ct);
            subjectId = await _db.Topics.Where(t => t.Id == topicId).Select(t => (Guid?)t.SubjectId).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException("Topic", topicId!);
        }

        // Only one live session at a time: pause any other active session.
        var others = await _db.StudySessions.Where(s => s.StudentId == studentId && s.Status == StudySessionStatus.Active).ToListAsync(ct);
        foreach (var o in others) PauseInternal(o);

        var attempt = lesson == null ? 1 : (await _db.StudentLessonProgress.Where(p => p.StudentId == studentId && p.LessonId == lesson.Id).Select(p => (int?)p.Attempts).FirstOrDefaultAsync(ct) ?? 0) + 1;
        var session = new StudySession
        {
            StudentId = studentId,
            Type = type,
            LessonId = lesson?.Id,
            SubjectId = subjectId!.Value,
            TopicId = topicId,
            SubTopicId = lesson?.SubTopicId,
            DailyStudySlotId = slot?.Id,
            StudyPlanItemId = planItem?.Id,
            AttemptNumber = attempt,
            Status = StudySessionStatus.Active,
            StartedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            PassThresholdPercent = await _progress.GetPassThresholdAsync(studentId, subjectId.Value, ct)
        };

        var activities = lesson != null
            ? await BuildLessonActivitiesAsync(session, lesson, studentId, attempt, ct)
            : await BuildQuestionSetActivitiesAsync(session, studentId, topicId!.Value, lessonId, request.QuestionCount ?? 10, ct);
        foreach (var a in activities) session.Activities.Add(a);

        var questionIds = activities.Where(a => a.QuestionId.HasValue).Select(a => a.QuestionId!.Value).ToList();
        session.TotalQuestions = questionIds.Count;
        session.MaxScore = questionIds.Count == 0 ? 0 : await _db.Questions.Where(q => questionIds.Contains(q.Id)).SumAsync(q => q.MaxMarks, ct);
        var first = activities.OrderBy(a => a.SortOrder).FirstOrDefault();
        if (first != null) { first.Status = SessionActivityStatus.Current; first.StartedAt = DateTime.UtcNow; session.CurrentActivityId = first.Id; session.CurrentQuestionId = first.QuestionId; }

        _db.StudySessions.Add(session);
        if (slot != null) { slot.Status = DailySlotStatus.InProgress; slot.SessionId = session.Id; }
        if (planItem != null && planItem.Status == StudyPlanItemStatus.Pending) planItem.Status = StudyPlanItemStatus.InProgress;

        if (lesson != null)
        {
            var lp = await _db.StudentLessonProgress.FirstOrDefaultAsync(p => p.StudentId == studentId && p.LessonId == lesson.Id, ct);
            if (lp == null) { lp = new StudentLessonProgress { StudentId = studentId, LessonId = lesson.Id, FirstStartedAt = DateTime.UtcNow, Status = LessonProgressStatus.InProgress }; _db.StudentLessonProgress.Add(lp); }
            else if (!lp.Passed) lp.Status = LessonProgressStatus.InProgress;
            lp.LastSessionId = session.Id;
        }
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Session.Start", "StudySession", session.Id.ToString(), new { session.Type, session.LessonId, session.TopicId, attempt }, true, ct);
        return await BuildSessionDtoAsync(session.Id, ct);
    }

    private async Task<List<SessionActivity>> BuildLessonActivitiesAsync(StudySession session, Lesson lesson, Guid studentId, int attempt, CancellationToken ct)
    {
        var list = new List<SessionActivity>();
        var order = 0;
        // Alternate question pool for re-attempts: practice questions on this lesson not yet answered correctly.
        List<Question> pool = new();
        if (attempt > 1)
        {
            var activityQuestionIds = lesson.Activities.Where(a => a.QuestionId.HasValue).Select(a => a.QuestionId!.Value).ToList();
            var correctlyAnswered = await _db.StudentAnswers.Where(a => a.StudentId == studentId && a.IsCorrect).Select(a => a.QuestionId).Distinct().ToListAsync(ct);
            pool = await _db.Questions.Where(q => q.LessonId == lesson.Id && q.Status == ContentStatus.Published && !activityQuestionIds.Contains(q.Id) && !correctlyAnswered.Contains(q.Id))
                .OrderBy(q => q.Difficulty).ToListAsync(ct);
        }
        var used = new HashSet<Guid>();
        foreach (var a in lesson.Activities.OrderBy(a => a.SortOrder))
        {
            order++;
            var qid = a.QuestionId;
            if (attempt > 1 && qid.HasValue && pool.Count > 0)
            {
                var original = await _db.Questions.Where(q => q.Id == qid).Select(q => new { q.Difficulty, q.QuestionType }).FirstOrDefaultAsync(ct);
                var alt = pool.Where(p => !used.Contains(p.Id)).OrderBy(p => Math.Abs(p.Difficulty - (original?.Difficulty ?? 3))).ThenBy(p => p.QuestionType == original?.QuestionType ? 0 : 1).FirstOrDefault();
                if (alt != null && (attempt % 2 == 0 || original == null)) { qid = alt.Id; used.Add(alt.Id); }
            }
            list.Add(new SessionActivity { SessionId = session.Id, LessonActivityId = a.Id, QuestionId = qid, SortOrder = order });
        }
        return list;
    }

    private async Task<List<SessionActivity>> BuildQuestionSetActivitiesAsync(StudySession session, Guid studentId, Guid topicId, Guid? lessonId, int count, CancellationToken ct)
    {
        count = Math.Clamp(count, 3, 30);
        var poolQ = _db.Questions.Where(q => q.Status == ContentStatus.Published && q.SubTopic.TopicId == topicId);
        if (lessonId.HasValue) poolQ = poolQ.Where(q => q.LessonId == lessonId);
        var pool = await poolQ.Select(q => new { q.Id, q.Difficulty, q.IsExamStyle }).ToListAsync(ct);
        if (pool.Count == 0) throw new BusinessRuleException("NO_QUESTIONS_AVAILABLE", "There are no questions available for this topic yet.");

        var history = await _db.StudentAnswers.Where(a => a.StudentId == studentId && a.AttemptNumber == 1 && a.Question.SubTopic.TopicId == topicId)
            .GroupBy(a => a.QuestionId).Select(g => new { QuestionId = g.Key, LastCorrect = g.OrderByDescending(a => a.AnsweredAt).First().IsCorrect, Count = g.Count() }).ToListAsync(ct);
        var wrong = history.Where(h => !h.LastCorrect).Select(h => h.QuestionId).ToHashSet();
        var seen = history.Select(h => h.QuestionId).ToHashSet();
        var rnd = new Random(unchecked(session.Id.GetHashCode() ^ studentId.GetHashCode()));
        var ordered = pool
            .OrderBy(q => wrong.Contains(q.Id) ? 0 : seen.Contains(q.Id) ? 2 : 1) // previously wrong first, then unseen, then seen-correct
            .ThenBy(_ => rnd.Next()).Take(count)
            .OrderBy(q => q.Difficulty).ThenBy(q => q.IsExamStyle ? 1 : 0).ToList();
        var order = 0;
        return ordered.Select(q => new SessionActivity { SessionId = session.Id, QuestionId = q.Id, SortOrder = ++order }).ToList();
    }

    // =====================================================================
    // Read
    // =====================================================================

    public async Task<StudySessionDto> GetAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(sessionId, requireOwner: false, ct);
        return await BuildSessionDtoAsync(session.Id, ct);
    }

    public async Task<SessionSummaryDto?> GetActiveAsync(CancellationToken ct = default)
    {
        var studentId = await _access.GetCurrentStudentIdAsync(ct);
        return await _db.StudySessions.Where(s => s.StudentId == studentId && (s.Status == StudySessionStatus.Active || s.Status == StudySessionStatus.Paused))
            .OrderByDescending(s => s.LastActivityAt).Select(ProgressService.SessionSummaryProjection).FirstOrDefaultAsync(ct);
    }

    public async Task<string?> GetHintAsync(Guid sessionId, Guid questionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(sessionId, requireOwner: true, ct);
        if (!session.Activities.Any(a => a.QuestionId == questionId)) throw new NotFoundException("Question", questionId);
        return await _db.Questions.Where(q => q.Id == questionId).Select(q => q.Hint).FirstOrDefaultAsync(ct)
            ?? "Re-read the explanation above and break the question into smaller steps.";
    }

    // =====================================================================
    // Answer
    // =====================================================================

    public async Task<AnswerResultDto> SubmitAnswerAsync(Guid sessionId, SubmitAnswerRequest request, CancellationToken ct = default)
    {
        var session = await LoadAsync(sessionId, requireOwner: true, ct);
        if (session.Status == StudySessionStatus.Paused) await ResumeInternalAsync(session, ct);
        if (session.Status != StudySessionStatus.Active) throw new BusinessRuleException("SESSION_NOT_ACTIVE", "This session is not active.");

        var activity = session.Activities.FirstOrDefault(a => a.QuestionId == request.QuestionId)
            ?? throw new NotFoundException("QUESTION_NOT_IN_SESSION", "That question is not part of this session.", true);
        var question = await _db.Questions.Include(q => q.Options).Include(q => q.AcceptedAnswers).Include(q => q.MarkScheme)
            .FirstAsync(q => q.Id == request.QuestionId, ct);

        var previous = session.Answers.Where(a => a.QuestionId == question.Id).OrderByDescending(a => a.AttemptNumber).FirstOrDefault();
        var attemptNumber = (previous?.AttemptNumber ?? 0) + 1;
        var result = _marking.Mark(question, request.AnswerText, request.AnswerJson);

        var answer = new StudentAnswer
        {
            SessionId = session.Id, StudentId = session.StudentId, QuestionId = question.Id, SessionActivityId = activity.Id,
            AttemptNumber = attemptNumber, AnswerText = request.AnswerText?.Trim(),
            AnswerJson = request.AnswerJson.HasValue && request.AnswerJson.Value.ValueKind != JsonValueKind.Undefined && request.AnswerJson.Value.ValueKind != JsonValueKind.Null ? request.AnswerJson.Value.GetRawText() : null,
            IsCorrect = result.Correct, Score = result.Score, MaxScore = result.MaxScore, TimeSpentSeconds = Math.Max(0, request.TimeSpentSeconds),
            Feedback = result.Feedback, MissingCriteriaJson = result.MissingCriteria.Count == 0 ? null : JsonSerializer.Serialize(result.MissingCriteria),
            MarkedBy = result.MarkedBy, HintUsed = request.HintUsed
        };
        _db.StudentAnswers.Add(answer);
        session.Answers.Add(answer);

        // First attempt counts towards the session score.
        if (attemptNumber == 1)
        {
            session.QuestionsAnswered++;
            if (result.Correct) session.QuestionsCorrect++;
            session.CurrentScore += result.Score;
            activity.Status = SessionActivityStatus.Completed;
            activity.CompletedAt = DateTime.UtcNow;
            activity.TimeSpentSeconds += Math.Max(0, request.TimeSpentSeconds);
        }
        else if (result.Correct && previous != null && !previous.IsCorrect)
        {
            // A later correct attempt marks the mistake as reviewed (learning credit, no score change).
            foreach (var p in session.Answers.Where(a => a.QuestionId == question.Id && a.Id != answer.Id)) p.IsReviewed = true;
        }

        session.ElapsedSeconds += Math.Max(0, request.TimeSpentSeconds);
        session.LastActivityAt = DateTime.UtcNow;
        var next = AdvanceIfComplete(session, activity);
        RecalculateProgress(session);
        await _db.SaveChangesAsync(ct);

        var isLast = !session.Activities.Any(a => a.Status is SessionActivityStatus.Pending or SessionActivityStatus.Current);
        var nextAction = isLast ? "Complete" : !result.Correct && attemptNumber == 1 ? "ReviewOrContinue" : "Continue";
        var showModel = attemptNumber >= 1;
        return new AnswerResultDto(answer.Id, question.Id, result.Correct, result.Score, result.MaxScore, result.Feedback, result.MissingCriteria,
            showModel ? question.Explanation : null, showModel ? result.CorrectAnswer : null, result.MarkedBy.ToString(), attemptNumber, nextAction, ToProgress(session));
    }

    // =====================================================================
    // Navigation / state
    // =====================================================================

    public async Task<StudySessionDto> NavigateAsync(Guid sessionId, NavigateRequest request, CancellationToken ct = default)
    {
        var session = await LoadAsync(sessionId, requireOwner: true, ct);
        if (session.Status == StudySessionStatus.Paused) await ResumeInternalAsync(session, ct);
        if (session.Status != StudySessionStatus.Active) throw new BusinessRuleException("SESSION_NOT_ACTIVE", "This session is not active.");

        var ordered = session.Activities.OrderBy(a => a.SortOrder).ToList();
        var current = ordered.FirstOrDefault(a => a.Id == session.CurrentActivityId) ?? ordered.First();
        SessionActivity? target;
        var direction = (request.Direction ?? (request.ActivityId.HasValue ? "goto" : "next")).ToLowerInvariant();
        switch (direction)
        {
            case "previous":
            case "prev":
                target = ordered.LastOrDefault(a => a.SortOrder < current.SortOrder) ?? current;
                break;
            case "goto":
                target = ordered.FirstOrDefault(a => a.Id == request.ActivityId) ?? throw new NotFoundException("Activity", request.ActivityId!);
                // Cannot jump forward past an unanswered question.
                var blocker = ordered.Where(a => a.SortOrder < target.SortOrder && a.SortOrder >= current.SortOrder && a.QuestionId.HasValue && a.Status != SessionActivityStatus.Completed).FirstOrDefault();
                if (blocker != null) throw new BusinessRuleException("ANSWER_REQUIRED", "Answer the current question before moving on.");
                break;
            default: // next
                if (current.QuestionId.HasValue && current.Status != SessionActivityStatus.Completed)
                    throw new BusinessRuleException("ANSWER_REQUIRED", "Answer this question before moving on.");
                target = ordered.FirstOrDefault(a => a.SortOrder > current.SortOrder);
                if (target == null) { MarkDone(current); RecalculateProgress(session); await _db.SaveChangesAsync(ct); return await BuildSessionDtoAsync(session.Id, ct); }
                break;
        }
        // Moving forward completes non-question activities that were passed.
        foreach (var a in ordered.Where(a => a.SortOrder < target!.SortOrder && !a.QuestionId.HasValue && a.Status != SessionActivityStatus.Completed)) MarkDone(a);
        if (!current.QuestionId.HasValue && target!.SortOrder > current.SortOrder) MarkDone(current);
        else if (current.Status == SessionActivityStatus.Current) current.Status = current.QuestionId.HasValue && current.Status == SessionActivityStatus.Completed ? SessionActivityStatus.Completed : SessionActivityStatus.Pending;

        if (target!.Status != SessionActivityStatus.Completed) { target.Status = SessionActivityStatus.Current; target.StartedAt ??= DateTime.UtcNow; }
        session.CurrentActivityId = target.Id;
        session.CurrentQuestionId = target.QuestionId;
        session.LastActivityAt = DateTime.UtcNow;
        RecalculateProgress(session);
        await _db.SaveChangesAsync(ct);
        return await BuildSessionDtoAsync(session.Id, ct);
    }

    public async Task<SessionProgressDto> HeartbeatAsync(Guid sessionId, SessionHeartbeatRequest request, CancellationToken ct = default)
    {
        var session = await LoadAsync(sessionId, requireOwner: true, ct);
        if (session.Status != StudySessionStatus.Active) return ToProgress(session);
        var now = DateTime.UtcNow;
        var sinceLast = (int)Math.Max(0, (now - (session.LastActivityAt ?? session.StartedAt ?? now)).TotalSeconds);
        if (request.ElapsedSeconds.HasValue)
            session.ElapsedSeconds = Math.Max(session.ElapsedSeconds, Math.Min(request.ElapsedSeconds.Value, session.ElapsedSeconds + sinceLast + 90));
        else session.ElapsedSeconds += Math.Min(sinceLast, 120);
        if (request.CurrentActivityId.HasValue && session.Activities.Any(a => a.Id == request.CurrentActivityId))
        {
            session.CurrentActivityId = request.CurrentActivityId;
            session.CurrentQuestionId = session.Activities.First(a => a.Id == request.CurrentActivityId).QuestionId;
        }
        if (request.ClientState.HasValue && request.ClientState.Value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            var raw = request.ClientState.Value.GetRawText();
            session.ClientStateJson = raw.Length > 8000 ? raw[..8000] : raw;
        }
        session.LastActivityAt = now;
        await _db.SaveChangesAsync(ct);
        return ToProgress(session);
    }

    public async Task<StudySessionDto> PauseAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(sessionId, requireOwner: true, ct);
        if (session.Status == StudySessionStatus.Active) { PauseInternal(session); await _db.SaveChangesAsync(ct); }
        else if (session.Status != StudySessionStatus.Paused) throw new BusinessRuleException("SESSION_NOT_ACTIVE", "Only an active session can be paused.");
        return await BuildSessionDtoAsync(session.Id, ct);
    }

    public async Task<StudySessionDto> ResumeAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(sessionId, requireOwner: true, ct);
        if (session.Status == StudySessionStatus.Paused) await ResumeInternalAsync(session, ct);
        else if (session.Status != StudySessionStatus.Active) throw new BusinessRuleException("SESSION_NOT_RESUMABLE", "This session can no longer be resumed.");
        return await BuildSessionDtoAsync(session.Id, ct);
    }

    public async Task AbandonAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(sessionId, requireOwner: true, ct);
        if (session.Status is StudySessionStatus.Completed) throw new BusinessRuleException("SESSION_COMPLETED", "A completed session cannot be abandoned.");
        session.Status = StudySessionStatus.Abandoned;
        session.LastActivityAt = DateTime.UtcNow;
        if (session.DailyStudySlotId.HasValue)
        {
            var slot = await _db.DailyStudySlots.FirstOrDefaultAsync(s => s.Id == session.DailyStudySlotId, ct);
            if (slot != null && slot.Status == DailySlotStatus.InProgress) { slot.Status = DailySlotStatus.Scheduled; slot.SessionId = null; }
        }
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Session.Abandon", "StudySession", session.Id.ToString(), null, true, ct);
    }

    // =====================================================================
    // Complete / results
    // =====================================================================

    public async Task<SessionResultDto> CompleteAsync(Guid sessionId, bool force, CancellationToken ct = default)
    {
        var session = await LoadAsync(sessionId, requireOwner: true, ct);
        if (session.Status == StudySessionStatus.Completed) return await GetResultAsync(sessionId, ct);
        if (session.Status is StudySessionStatus.Abandoned) throw new BusinessRuleException("SESSION_ABANDONED", "This session was abandoned.");

        var unanswered = session.Activities.Count(a => a.QuestionId.HasValue && a.Status != SessionActivityStatus.Completed);
        if (unanswered > 0 && !force)
            throw new BusinessRuleException("SESSION_INCOMPLETE", $"There {(unanswered == 1 ? "is 1 question" : $"are {unanswered} questions")} still to answer. Finish them or complete with force=true to submit as-is.");

        foreach (var a in session.Activities.Where(a => a.Status != SessionActivityStatus.Completed))
            a.Status = a.QuestionId.HasValue ? SessionActivityStatus.Skipped : SessionActivityStatus.Completed;

        session.Status = StudySessionStatus.Completed;
        session.CompletedAt = DateTime.UtcNow;
        session.LastActivityAt = session.CompletedAt;
        session.PausedAt = null;
        session.ProgressPercentage = 100;
        session.ScorePercent = session.MaxScore == 0 ? null : Math.Round(session.CurrentScore / session.MaxScore * 100, 1);
        session.Passed = session.MaxScore == 0 ? null : session.ScorePercent >= session.PassThresholdPercent;
        await _db.SaveChangesAsync(ct);

        await _progress.RecordSessionCompletionAsync(session, ct);
        await _audit.LogAsync("Session.Complete", "StudySession", session.Id.ToString(), new { session.ScorePercent, session.Passed, session.ElapsedSeconds }, true, ct);
        return await GetResultAsync(sessionId, ct);
    }

    public async Task<SessionResultDto> GetResultAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await LoadAsync(sessionId, requireOwner: false, ct);
        if (session.Status != StudySessionStatus.Completed) throw new BusinessRuleException("SESSION_NOT_COMPLETED", "Results are available once the session is completed.");
        var summary = await _db.StudySessions.Where(s => s.Id == sessionId).Select(ProgressService.SessionSummaryProjection).FirstAsync(ct);
        summary = await FillSubjectAsync(summary, session, ct);

        var scorePercent = session.ScorePercent ?? 0;
        var passed = session.Passed ?? true;

        // Performance bands by question type family and exam-style questions.
        var firstAnswers = session.Answers.Where(a => a.AttemptNumber == 1).ToList();
        var qIds = firstAnswers.Select(a => a.QuestionId).ToList();
        var qInfo = await _db.Questions.Where(q => qIds.Contains(q.Id)).Select(q => new { q.Id, q.QuestionType, q.IsExamStyle, q.Difficulty }).ToListAsync(ct);
        var bands = new List<PerformanceBandDto>();
        void Band(string name, Func<Guid, bool> pred)
        {
            var set = firstAnswers.Where(a => pred(a.QuestionId)).ToList();
            if (set.Count == 0) return;
            var max = set.Sum(a => a.MaxScore); var pct = max == 0 ? 0 : Math.Round(set.Sum(a => a.Score) / max * 100, 0);
            bands.Add(new PerformanceBandDto(name, pct, Rate(pct)));
        }
        Band("Recall and understanding", id => qInfo.Any(q => q.Id == id && !q.IsExamStyle && q.QuestionType is QuestionType.MultipleChoice or QuestionType.TrueFalse or QuestionType.FillInTheBlank or QuestionType.Matching or QuestionType.Ordering or QuestionType.MultipleAnswer or QuestionType.SingleAnswer));
        Band("Calculations", id => qInfo.Any(q => q.Id == id && !q.IsExamStyle && q.QuestionType is QuestionType.NumericalAnswer or QuestionType.FormulaCalculation or QuestionType.Equation));
        Band("Written explanations", id => qInfo.Any(q => q.Id == id && !q.IsExamStyle && q.QuestionType is QuestionType.ShortAnswer or QuestionType.LongAnswer));
        Band("Exam-style questions", id => qInfo.Any(q => q.Id == id && q.IsExamStyle));

        var mistakes = (await _progress.GetMistakesAsync(session.StudentId, null, session.Id, new PagingQuery(1, 100), ct)).Items;

        // Next lesson
        Guid? nextId = null; string? nextTitle = null; var unlocked = false; var canRetry = false;
        if (session.LessonId.HasValue)
        {
            var lesson = await _db.Lessons.Include(l => l.SubTopic).FirstAsync(l => l.Id == session.LessonId, ct);
            var lessons = await _progress.GetLessonsAsync(session.StudentId, lesson.SubTopic.TopicId, ct);
            var idx = lessons.ToList().FindIndex(l => l.LessonId == lesson.Id);
            var next = idx >= 0 && idx + 1 < lessons.Count ? lessons[idx + 1] : null;
            if (next != null) { nextId = next.LessonId; nextTitle = next.Title; unlocked = next.Status != "Locked"; }
            else unlocked = passed;
            var maxAttempts = await _db.StudentSubjectSettings.Where(s => s.StudentId == session.StudentId && s.SubjectId == session.SubjectId).Select(s => (int?)s.MaxAttemptsBeforeMoveOn).FirstOrDefaultAsync(ct) ?? 3;
            canRetry = !passed || scorePercent < 100;
            if (!passed && maxAttempts > 0 && session.AttemptNumber >= maxAttempts) unlocked = true;
        }
        else { unlocked = true; canRetry = true; }

        var message = passed
            ? scorePercent >= 90 ? "Outstanding! You have mastered this material." : "Well done, you passed! Keep the momentum going."
            : $"You scored {Math.Round(scorePercent)}%, below the {session.PassThresholdPercent}% needed to move on. Review your mistakes and try again, you're closer than you think.";

        return new SessionResultDto(summary, session.CurrentScore, session.MaxScore, scorePercent, passed, session.PassThresholdPercent, session.AttemptNumber,
            canRetry, unlocked, nextId, nextTitle, bands, mistakes, message);
    }

    private static string Rate(decimal pct) => pct >= 85 ? "Strong" : pct >= 65 ? "Secure" : pct >= 45 ? "Developing" : "Needs practice";

    // =====================================================================
    // Internals
    // =====================================================================

    private async Task<StudySession> LoadAsync(Guid sessionId, bool requireOwner, CancellationToken ct)
    {
        var session = await _db.StudySessions.Include(s => s.Activities).Include(s => s.Answers).FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException("SESSION_NOT_FOUND", "Study session could not be found.", true);
        if (requireOwner)
        {
            var studentId = await _access.GetCurrentStudentIdAsync(ct);
            if (session.StudentId != studentId) throw new ForbiddenException("This session belongs to another student.");
        }
        else if (!await _access.CanAccessStudentAsync(session.StudentId, ct)) throw new ForbiddenException();
        return session;
    }

    private static void PauseInternal(StudySession s)
    {
        s.Status = StudySessionStatus.Paused;
        s.PausedAt = DateTime.UtcNow;
        s.LastActivityAt = DateTime.UtcNow;
    }

    private async Task ResumeInternalAsync(StudySession s, CancellationToken ct)
    {
        var others = await _db.StudySessions.Where(o => o.StudentId == s.StudentId && o.Id != s.Id && o.Status == StudySessionStatus.Active).ToListAsync(ct);
        foreach (var o in others) PauseInternal(o);
        s.Status = StudySessionStatus.Active;
        s.ResumedAt = DateTime.UtcNow;
        s.PausedAt = null;
        s.LastActivityAt = DateTime.UtcNow;
    }

    private static void MarkDone(SessionActivity a)
    {
        if (a.Status == SessionActivityStatus.Completed) return;
        a.Status = SessionActivityStatus.Completed;
        a.CompletedAt = DateTime.UtcNow;
        if (a.StartedAt.HasValue) a.TimeSpentSeconds += (int)Math.Min(600, (DateTime.UtcNow - a.StartedAt.Value).TotalSeconds);
    }

    private static SessionActivity? AdvanceIfComplete(StudySession session, SessionActivity answered)
    {
        if (answered.Status != SessionActivityStatus.Completed) return null;
        if (session.CurrentActivityId != answered.Id) return null;
        var next = session.Activities.Where(a => a.SortOrder > answered.SortOrder).OrderBy(a => a.SortOrder).FirstOrDefault();
        // Keep the answered question current so the client can show feedback; expose the next id via progress.
        return next;
    }

    private static void RecalculateProgress(StudySession s)
    {
        var total = s.Activities.Count;
        var done = s.Activities.Count(a => a.Status is SessionActivityStatus.Completed or SessionActivityStatus.Skipped);
        s.ProgressPercentage = total == 0 ? 0 : Math.Round(done / (decimal)total * 100, 1);
    }

    private static SessionProgressDto ToProgress(StudySession s) =>
        new(s.Id, s.Status.ToString(), s.ProgressPercentage, s.ElapsedSeconds, s.Activities.Count,
            s.Activities.Count(a => a.Status is SessionActivityStatus.Completed or SessionActivityStatus.Skipped),
            s.TotalQuestions, s.QuestionsAnswered, s.QuestionsCorrect, s.CurrentScore, s.MaxScore, s.CurrentActivityId);

    private async Task<SessionSummaryDto> FillSubjectAsync(SessionSummaryDto dto, StudySession s, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(dto.SubjectName) && (dto.TopicName != null || s.TopicId == null)) return dto;
        var subject = await _db.Subjects.Where(x => x.Id == s.SubjectId).Select(x => new { x.Name, x.ColourHex }).FirstAsync(ct);
        var topicName = s.TopicId.HasValue ? await _db.Topics.Where(t => t.Id == s.TopicId).Select(t => t.Name).FirstOrDefaultAsync(ct) : null;
        return dto with { SubjectName = subject.Name, SubjectColour = subject.ColourHex, TopicName = topicName ?? dto.TopicName };
    }

    private async Task<StudySessionDto> BuildSessionDtoAsync(Guid sessionId, CancellationToken ct)
    {
        var s = await _db.StudySessions.Include(x => x.Activities).ThenInclude(a => a.LessonActivity).Include(x => x.Answers)
            .Include(x => x.Lesson).FirstAsync(x => x.Id == sessionId, ct);
        var subject = await _db.Subjects.Where(x => x.Id == s.SubjectId).Select(x => new { x.Name, x.ColourHex }).FirstAsync(ct);
        var topic = s.TopicId.HasValue ? await _db.Topics.Where(t => t.Id == s.TopicId).Select(t => t.Name).FirstOrDefaultAsync(ct) : null;
        var subTopic = s.SubTopicId.HasValue ? await _db.SubTopics.Where(t => t.Id == s.SubTopicId).Select(t => t.Name).FirstOrDefaultAsync(ct) : null;

        var qIds = s.Activities.Where(a => a.QuestionId.HasValue).Select(a => a.QuestionId!.Value).ToList();
        var questions = await _db.Questions.Include(q => q.Options).Include(q => q.AcceptedAnswers).Where(q => qIds.Contains(q.Id)).ToListAsync(ct);

        var activities = new List<SessionActivityDto>();
        foreach (var a in s.Activities.OrderBy(a => a.SortOrder))
        {
            QuestionDto? qdto = null; AnswerSummaryDto? ans = null;
            if (a.QuestionId.HasValue)
            {
                var q = questions.First(x => x.Id == a.QuestionId);
                qdto = RenderQuestion(q, s.Id);
                var last = s.Answers.Where(x => x.QuestionId == q.Id).OrderByDescending(x => x.AttemptNumber).FirstOrDefault();
                var first = s.Answers.Where(x => x.QuestionId == q.Id).OrderBy(x => x.AttemptNumber).FirstOrDefault();
                if (first != null) ans = new AnswerSummaryDto(first.Id, first.IsCorrect, first.Score, first.MaxScore, first.Feedback, first.AnswerText, ProgressService.ParseJson(first.AnswerJson), last!.AttemptNumber);
            }
            var la = a.LessonActivity;
            var type = la?.Type.ToString() ?? "Question";
            var title = la?.Title ?? (s.Type == StudySessionType.Review ? $"Review question {a.SortOrder}" : $"Practice question {a.SortOrder}");
            activities.Add(new SessionActivityDto(a.Id, a.SortOrder, type, title, a.Status.ToString(), la?.ContentMarkdown, la?.EstimatedMinutes ?? 3, la?.IsCheckpoint ?? false, qdto, ans));
        }
        var current = activities.FirstOrDefault(a => a.Id == s.CurrentActivityId);
        var estimated = s.Lesson?.EstimatedMinutes ?? Math.Max(10, s.TotalQuestions * 3);
        return new StudySessionDto(s.Id, s.Type.ToString(), s.Status.ToString(), s.AttemptNumber, s.StudentId, s.SubjectId, subject.Name, subject.ColourHex,
            s.TopicId, topic, s.SubTopicId, subTopic, s.LessonId, s.Lesson?.Title, estimated, s.PassThresholdPercent,
            s.StartedAt, s.PausedAt, s.CompletedAt, s.LastActivityAt, ToProgress(s), current, activities, ProgressService.ParseJson(s.ClientStateJson));
    }

    /// <summary>Renders a question for the client without revealing answers. Ordering/matching options are shuffled deterministically per session.</summary>
    public static QuestionDto RenderQuestion(Question q, Guid sessionId)
    {
        var rnd = new Random(unchecked(sessionId.GetHashCode() ^ q.Id.GetHashCode()));
        IEnumerable<QuestionOption> opts = q.Options.OrderBy(o => o.SortOrder);
        if (q.QuestionType == QuestionType.Ordering) opts = q.Options.OrderBy(_ => rnd.Next());
        var options = opts.Select((o, i) => new QuestionOptionDto(o.Id, o.Text, i)).ToList();

        IReadOnlyList<string>? targets = null; string? blanksText = null; JsonElement? meta = null;
        if (!string.IsNullOrWhiteSpace(q.MetadataJson))
        {
            try
            {
                var doc = JsonDocument.Parse(q.MetadataJson).RootElement.Clone();
                meta = doc;
                if (doc.TryGetProperty("blanksText", out var bt)) blanksText = bt.GetString();
                if (doc.TryGetProperty("matchTargets", out var mt) && mt.ValueKind == JsonValueKind.Array) targets = mt.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
            }
            catch { /* ignore bad metadata */ }
        }
        if (q.QuestionType is QuestionType.Matching or QuestionType.DragAndDrop or QuestionType.DiagramLabelling)
            targets = (targets ?? q.Options.Select(o => o.MatchKey ?? "").Distinct().ToList()).OrderBy(_ => rnd.Next()).ToList();
        var blankCount = q.QuestionType == QuestionType.FillInTheBlank ? Math.Max(1, q.AcceptedAnswers.Select(a => a.BlankIndex ?? 0).DefaultIfEmpty(0).Max() + 1) : 0;

        return new QuestionDto(q.Id, q.QuestionType.ToString(), q.QuestionText, q.MaxMarks, q.Difficulty, q.IsExamStyle, q.ImageUrl, q.TimeLimitSeconds,
            !string.IsNullOrWhiteSpace(q.Hint), options, targets, blanksText, blankCount, meta);
    }
}
