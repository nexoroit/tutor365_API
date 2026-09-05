using System.Text;
using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

public interface IAiTutorService
{
    Task<AiTutorResponse> AskAsync(AiTutorRequest request, CancellationToken ct = default);
    Task<AiConversationDto> GetConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task<PagedResult<AiConversationDto>> GetConversationsAsync(Guid studentId, PagingQuery paging, CancellationToken ct = default);
}

/// <summary>Server-side AI tutor. Builds controlled curriculum context and calls the configured provider (stub until a vendor is chosen).
/// When the provider is disabled, deterministic guidance from the question bank (hints, explanations, model answers) is returned.</summary>
public class AiTutorService : IAiTutorService
{
    private readonly IAppDbContext _db;
    private readonly IAccessService _access;
    private readonly IAiProvider _ai;
    private readonly IMarkingService _marking;
    private readonly IAiSettingsService _settings;

    public AiTutorService(IAppDbContext db, IAccessService access, IAiProvider ai, IMarkingService marking, IAiSettingsService settings)
    {
        _db = db; _access = access; _ai = ai; _marking = marking; _settings = settings;
    }

    public async Task<AiTutorResponse> AskAsync(AiTutorRequest request, CancellationToken ct = default)
    {
        var studentId = await _access.GetCurrentStudentIdAsync(ct);
        var intent = (request.Intent ?? "message").ToLowerInvariant();
        var aiEnabled = await _ai.IsEnabledAsync(ct);
        if (aiEnabled)
        {
            var cfg = await _settings.GetConfigAsync(ct);
            var since = DateTime.UtcNow.Date;
            var usedToday = await _db.AIConversationMessages.CountAsync(m => m.Conversation.StudentId == studentId && m.Role == AiMessageRole.Student && m.CreatedAt >= since, ct);
            if (usedToday >= cfg.DailyMessageLimit)
                throw new BusinessRuleException("AI_DAILY_LIMIT", $"You've used today's {cfg.DailyMessageLimit} tutor messages. Use the hints and explanations in the lesson, and try again tomorrow.");
        }
        var student = await _db.Students.Include(s => s.User).Include(s => s.YearGroup).Include(s => s.ExamBoard).FirstAsync(s => s.Id == studentId, ct);

        StudySession? session = null;
        if (request.SessionId.HasValue)
        {
            session = await _db.StudySessions.FirstOrDefaultAsync(s => s.Id == request.SessionId && s.StudentId == studentId, ct)
                ?? throw new NotFoundException("SESSION_NOT_FOUND", "Study session could not be found.", true);
            if (session.Type is StudySessionType.Assessment or StudySessionType.Mock && session.Status != StudySessionStatus.Completed)
                throw new BusinessRuleException("NO_TUTOR_IN_ASSESSMENT", "The tutor isn't available during a test. You can ask about any question once you've submitted it.");
        }
        var questionId = request.QuestionId ?? session?.CurrentQuestionId;
        Question? question = questionId.HasValue
            ? await _db.Questions.Include(q => q.Options).Include(q => q.AcceptedAnswers).Include(q => q.MarkScheme).Include(q => q.SubTopic).ThenInclude(s => s.Topic).ThenInclude(t => t.Subject)
                .FirstOrDefaultAsync(q => q.Id == questionId, ct)
            : null;
        var lessonId = request.LessonId ?? session?.LessonId ?? question?.LessonId;
        var lesson = lessonId.HasValue ? await _db.Lessons.Include(l => l.Activities).Include(l => l.SubTopic).ThenInclude(s => s.Topic).ThenInclude(t => t.Subject).FirstOrDefaultAsync(l => l.Id == lessonId, ct) : null;

        // Conversation
        AIConversation conversation;
        if (request.ConversationId.HasValue)
            conversation = await _db.AIConversations.Include(c => c.Messages).FirstOrDefaultAsync(c => c.Id == request.ConversationId && c.StudentId == studentId, ct)
                ?? throw new NotFoundException("AIConversation", request.ConversationId);
        else
        {
            conversation = new AIConversation
            {
                StudentId = studentId, SessionId = session?.Id, LessonId = lesson?.Id, QuestionId = question?.Id,
                SubjectId = lesson?.SubTopic.Topic.SubjectId ?? question?.SubTopic.Topic.SubjectId ?? session?.SubjectId,
                TopicId = lesson?.SubTopic.TopicId ?? question?.SubTopic.TopicId ?? session?.TopicId
            };
            _db.AIConversations.Add(conversation);
        }

        var lastAnswer = question == null ? null
            : await _db.StudentAnswers.Where(a => a.StudentId == studentId && a.QuestionId == question.Id).OrderByDescending(a => a.AnsweredAt).FirstOrDefaultAsync(ct);
        var mastery = conversation.TopicId.HasValue
            ? await _db.StudentTopicProgress.Where(p => p.StudentId == studentId && p.TopicId == conversation.TopicId).Select(p => (decimal?)p.MasteryScore).FirstOrDefaultAsync(ct) : null;

        var studentText = request.Message ?? DefaultPrompt(intent);
        var studentMsg = new AIConversationMessage { ConversationId = conversation.Id, Role = AiMessageRole.Student, Message = studentText, Intent = intent };
        _db.AIConversationMessages.Add(studentMsg); if (!conversation.Messages.Contains(studentMsg)) conversation.Messages.Add(studentMsg);

        string reply; bool isStub;
        if (aiEnabled)
        {
            var system = BuildSystemPrompt(student, lesson, question, lastAnswer, mastery, intent);
            var history = conversation.Messages.OrderBy(m => m.CreatedAt).TakeLast(12)
                .Select(m => new AiChatMessage(m.Role == AiMessageRole.Student ? "user" : "assistant", m.Message)).ToList();
            var completion = await _ai.CompleteAsync(new AiRequest(system, history, 700, "tutor"), ct);
            reply = completion.IsStub ? FallbackReply(intent, question, lesson, lastAnswer, student.User.FirstName) : Sanitise(completion.Content, question);
            isStub = completion.IsStub;
            conversation.TotalTokens += completion.InputTokens + completion.OutputTokens;
        }
        else
        {
            reply = FallbackReply(intent, question, lesson, lastAnswer, student.User.FirstName);
            isStub = true;
        }

        var tutorMsg = new AIConversationMessage { ConversationId = conversation.Id, Role = AiMessageRole.Tutor, Message = reply, Intent = intent };
        _db.AIConversationMessages.Add(tutorMsg); if (!conversation.Messages.Contains(tutorMsg)) conversation.Messages.Add(tutorMsg);
        await _db.SaveChangesAsync(ct);

        var actions = new List<string> { "hint", "explain", "example" };
        if (lastAnswer != null && !lastAnswer.IsCorrect) actions.Insert(0, "why-wrong");
        if (question != null) actions.AddRange(new[] { "easier", "harder" });
        return new AiTutorResponse(conversation.Id, reply, intent, isStub, _ai.ProviderName, actions);
    }

    public async Task<AiConversationDto> GetConversationAsync(Guid conversationId, CancellationToken ct = default)
    {
        var c = await _db.AIConversations.Include(x => x.Messages).FirstOrDefaultAsync(x => x.Id == conversationId, ct) ?? throw new NotFoundException("AIConversation", conversationId);
        if (!await _access.CanAccessStudentAsync(c.StudentId, ct)) throw new ForbiddenException();
        return ToDto(c);
    }

    public async Task<PagedResult<AiConversationDto>> GetConversationsAsync(Guid studentId, PagingQuery paging, CancellationToken ct = default)
    {
        if (!await _access.CanAccessStudentAsync(studentId, ct)) throw new ForbiddenException();
        var page = await _db.AIConversations.Include(c => c.Messages).Where(c => c.StudentId == studentId).OrderByDescending(c => c.CreatedAt).ToPagedResultAsync(paging, ct);
        return new PagedResult<AiConversationDto> { Page = page.Page, PageSize = page.PageSize, TotalCount = page.TotalCount, Items = page.Items.Select(ToDto).ToList() };
    }

    private static AiConversationDto ToDto(AIConversation c) =>
        new(c.Id, c.StudentId, c.SessionId, c.LessonId, c.CreatedAt,
            c.Messages.Where(m => m.Role != AiMessageRole.System).OrderBy(m => m.CreatedAt).Select(m => new AiMessageDto(m.Id, m.Role.ToString(), m.Message, m.Intent, m.CreatedAt)).ToList());

    private static string DefaultPrompt(string intent) => intent switch
    {
        "hint" => "Can I have a hint?",
        "explain" => "Can you explain this differently?",
        "example" => "Can you show me an example?",
        "why-wrong" => "Why is my answer wrong?",
        "easier" => "Can I try an easier question?",
        "harder" => "Can I try a harder question?",
        _ => "I need some help."
    };

    private static string BuildSystemPrompt(Student student, Lesson? lesson, Question? question, StudentAnswer? lastAnswer, decimal? mastery, string intent)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are Tutor365, a patient one-to-one GCSE tutor for a UK student. Use British English.");
        sb.AppendLine($"Student: {student.User.FirstName}, {student.YearGroup.Name}, exam board {student.ExamBoard.Name}, target grade {student.TargetGrade}.");
        if (lesson != null)
        {
            sb.AppendLine($"Subject: {lesson.SubTopic.Topic.Subject.Name}. Topic: {lesson.SubTopic.Topic.Name}. Sub-topic: {lesson.SubTopic.Name}. Lesson: {lesson.Title}.");
            var explanation = lesson.Activities.Where(a => a.Type is LessonActivityType.Explanation or LessonActivityType.Example).OrderBy(a => a.SortOrder).Select(a => a.ContentMarkdown).Where(c => c != null);
            var ctx = string.Join("\n\n", explanation);
            if (ctx.Length > 6000) ctx = ctx[..6000];
            sb.AppendLine("Approved lesson content (stay within it; do not invent curriculum facts):\n" + ctx);
        }
        if (question != null)
        {
            sb.AppendLine($"Current question ({question.QuestionType}, {question.MaxMarks} marks): {question.QuestionText}");
            if (!string.IsNullOrWhiteSpace(question.Hint)) sb.AppendLine("Approved hint: " + question.Hint);
            if (!string.IsNullOrWhiteSpace(question.Explanation)) sb.AppendLine("Model explanation (never reveal verbatim unless intent is why-wrong after the student has answered): " + question.Explanation);
            if (question.MarkScheme.Count > 0) sb.AppendLine("Mark scheme: " + string.Join("; ", question.MarkScheme.Select(m => $"[{m.Marks}] {m.CriterionText}")));
        }
        if (lastAnswer != null)
            sb.AppendLine($"Student's last answer: \"{DescribeAnswer(lastAnswer, question)}\" scored {lastAnswer.Score:0.##}/{lastAnswer.MaxScore:0.##}.{(string.IsNullOrWhiteSpace(lastAnswer.MissingCriteriaJson) ? "" : " Missing: " + lastAnswer.MissingCriteriaJson)}");
        if (mastery.HasValue) sb.AppendLine($"Topic mastery: {Math.Round(mastery.Value * 100)}%.");
        sb.AppendLine($"Intent: {intent}. Rules: give hints before answers; never give the final answer to an unanswered question; ask a guiding question back; keep replies under 150 words; encourage; stay on the learning topic; if asked about anything unrelated to GCSE study, politely steer back.");
        return sb.ToString();
    }

    /// <summary>Human-readable version of a stored answer (option texts for choice questions, pairs for matching, raw text otherwise).</summary>
    private static string DescribeAnswer(StudentAnswer a, Question? q)
    {
        if (!string.IsNullOrWhiteSpace(a.AnswerText)) return a.AnswerText;
        if (string.IsNullOrWhiteSpace(a.AnswerJson) || q == null) return "(no answer)";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(a.AnswerJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("selectedOptionIds", out var ids) && ids.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var texts = ids.EnumerateArray().Select(e => Guid.TryParse(e.GetString(), out var g) ? q.Options.FirstOrDefault(o => o.Id == g)?.Text : null).Where(t => t != null).ToList();
                return texts.Count == 0 ? "(no option selected)" : string.Join("; ", texts);
            }
            if (root.TryGetProperty("blanks", out var blanks) && blanks.ValueKind == System.Text.Json.JsonValueKind.Array)
                return string.Join(" | ", blanks.EnumerateArray().Select(e => e.GetString()));
            if (root.TryGetProperty("pairs", out var pairs) && pairs.ValueKind == System.Text.Json.JsonValueKind.Array)
                return string.Join("; ", pairs.EnumerateArray().Select(p => $"{(p.TryGetProperty("optionId", out var oid) && Guid.TryParse(oid.GetString(), out var g) ? q.Options.FirstOrDefault(o => o.Id == g)?.Text : "?")} -> {(p.TryGetProperty("matchKey", out var mk) ? mk.GetString() : "?")}"));
            if (root.TryGetProperty("order", out var order) && order.ValueKind == System.Text.Json.JsonValueKind.Array)
                return string.Join(" -> ", order.EnumerateArray().Select(e => Guid.TryParse(e.GetString(), out var g) ? q.Options.FirstOrDefault(o => o.Id == g)?.Text ?? "?" : "?"));
            return a.AnswerJson;
        }
        catch { return a.AnswerJson; }
    }

    private static string Sanitise(string content, Question? question)
    {
        // Strip any accidental echo of the system prompt markers.
        return content.Replace("Approved lesson content", "").Trim();
    }

    private string FallbackReply(string intent, Question? q, Lesson? lesson, StudentAnswer? last, string name)
    {
        switch (intent)
        {
            case "hint":
                return q?.Hint ?? "Start by underlining the key words in the question, then decide which idea from the lesson they connect to.";
            case "why-wrong":
                if (q == null || last == null) return "Answer the question first and I'll go through it with you.";
                var missing = ProgressService.ParseList(last.MissingCriteriaJson);
                var sb = new StringBuilder($"Let's look at your answer, {name}. You scored {last.Score:0.##}/{last.MaxScore:0.##}. ");
                if (missing.Count > 0) sb.Append("What was missing: " + string.Join("; ", missing) + ". ");
                if (!string.IsNullOrWhiteSpace(q.Explanation)) sb.Append("\n\nHere's how to think about it: " + q.Explanation);
                var model = _marking.DescribeCorrectAnswer(q);
                if (model != null) sb.Append($"\n\nCorrect answer: {model}");
                return sb.ToString();
            case "explain":
                var expl = lesson?.Activities.Where(a => a.Type == LessonActivityType.Explanation).OrderBy(a => a.SortOrder).Select(a => a.ContentMarkdown).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
                return expl != null ? "Let's go over the key idea again:\n\n" + expl : "Re-read the explanation section of this lesson and note the one sentence that answers the question. Then try again.";
            case "example":
                var ex = lesson?.Activities.Where(a => a.Type == LessonActivityType.Example).OrderBy(a => a.SortOrder).Select(a => a.ContentMarkdown).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
                return ex != null ? "Here's a worked example:\n\n" + ex : "Look back at the worked example in this lesson and follow the same steps with the numbers in this question.";
            case "easier":
            case "harder":
                return $"Finish this question first. When you complete the session, start a Review session for this topic and I'll pick {(intent == "easier" ? "easier" : "more challenging")} questions based on your results.";
            default:
                return $"I'm here to help, {name}. The full AI tutor is coming soon. For now use the hint, the explanation and the worked example, and take the question one step at a time.";
        }
    }
}
