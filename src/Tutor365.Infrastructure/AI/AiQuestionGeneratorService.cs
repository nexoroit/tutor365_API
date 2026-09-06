using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Infrastructure.Data;
using Tutor365.Infrastructure.Data.Seed;
using static Tutor365.Infrastructure.Data.Seed.ContentSeeder;

namespace Tutor365.Infrastructure.AI;

/// <summary>
/// Writes new variant questions with the AI provider, in the same JSON shape as the content files, validates them and stores
/// them as published practice questions tagged "ai-variant" so the session builder can pick them and admins can review them.
/// </summary>
public class AiQuestionGeneratorService : IQuestionGenerator
{
    public const string Tag = "ai-variant";
    private static readonly QuestionType[] Supported =
    {
        QuestionType.MultipleChoice, QuestionType.TrueFalse, QuestionType.MultipleAnswer, QuestionType.FillInTheBlank, QuestionType.NumericalAnswer,
        QuestionType.FormulaCalculation, QuestionType.ShortAnswer, QuestionType.LongAnswer, QuestionType.ExamQuestion, QuestionType.Matching, QuestionType.Ordering,
    };
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true, WriteIndented = false, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly AppDbContext _db;
    private readonly IAiProvider _ai;
    private readonly ContentSeeder _seeder;
    private readonly ILogger<AiQuestionGeneratorService> _logger;
    public AiQuestionGeneratorService(AppDbContext db, IAiProvider ai, ContentSeeder seeder, ILogger<AiQuestionGeneratorService> logger) { _db = db; _ai = ai; _seeder = seeder; _logger = logger; }

    public async Task<int> GenerateVariantsAsync(Guid lessonId, Guid? studentId, int count = 1, CancellationToken ct = default)
    {
        if (!await SettingAsync("Questions.GenerateVariants", true, ct) || !await _ai.IsEnabledAsync(ct)) return 0;
        var maxPerDay = await SettingAsync("Questions.MaxGeneratedPerDay", 200, ct);
        var minUnseen = await SettingAsync("Questions.MinUnseenPerActivity", 1, ct);
        var today = DateTime.UtcNow.Date;
        var madeToday = await _db.Questions.CountAsync(q => q.Tags != null && q.Tags.Contains(Tag) && q.CreatedAt >= today, ct);

        var lesson = await _db.Lessons.Include(l => l.Activities).Include(l => l.SubTopic).ThenInclude(s => s.Topic).ThenInclude(t => t.Subject)
            .FirstOrDefaultAsync(l => l.Id == lessonId, ct);
        if (lesson == null) return 0;
        var activityQuestionIds = lesson.Activities.Where(a => a.QuestionId.HasValue).Select(a => a.QuestionId!.Value).ToList();
        var originals = await _db.Questions.Include(q => q.Options).Include(q => q.AcceptedAnswers).Include(q => q.MarkScheme).Where(q => activityQuestionIds.Contains(q.Id)).ToListAsync(ct);
        var pool = await _db.Questions.Where(q => q.LessonId == lessonId && q.Status == ContentStatus.Published && !activityQuestionIds.Contains(q.Id))
            .Select(q => new { q.Id, q.QuestionType, q.MaxMarks, q.Difficulty, q.QuestionText }).ToListAsync(ct);
        var seen = studentId.HasValue ? (await _db.StudentAnswers.Where(a => a.StudentId == studentId).Select(a => a.QuestionId).Distinct().ToListAsync(ct)).ToHashSet() : new HashSet<Guid>();
        var boardId = lesson.SubTopic.Topic.Qualification?.ExamBoardId ?? await _db.Qualifications.Where(q => q.Id == lesson.SubTopic.Topic.QualificationId).Select(q => q.ExamBoardId).FirstAsync(ct);
        var context = string.Join("\n\n", lesson.Activities.Where(a => a.Type is LessonActivityType.Explanation or LessonActivityType.Example).OrderBy(a => a.SortOrder).Select(a => a.ContentMarkdown).Where(c => !string.IsNullOrWhiteSpace(c)));
        if (context.Length > 3500) context = context[..3500];

        var made = 0;
        foreach (var activity in lesson.Activities.Where(a => a.QuestionId.HasValue).OrderBy(a => a.SortOrder))
        {
            var original = originals.FirstOrDefault(o => o.Id == activity.QuestionId);
            if (original == null || !Supported.Contains(original.QuestionType)) continue;
            var siblings = pool.Where(p => p.QuestionType == original.QuestionType && p.MaxMarks == original.MaxMarks && Math.Abs(p.Difficulty - original.Difficulty) <= 1).ToList();
            var unseen = siblings.Count(p => !seen.Contains(p.Id)) + (seen.Contains(original.Id) ? 0 : 1);
            var need = studentId.HasValue ? Math.Max(0, minUnseen - unseen) : count;
            for (var i = 0; i < need; i++)
            {
                if (madeToday >= maxPerDay) { _logger.LogWarning("Question generation daily cap ({Cap}) reached", maxPerDay); return made; }
                var existingStems = siblings.Select(p => p.QuestionText).Append(original.QuestionText).TakeLast(8).ToList();
                var cq = await GenerateOneAsync(original, lesson, context, existingStems, ct);
                if (cq == null) continue;
                var key = $"AI-{lessonId:N}"[..11] + $"-{activity.Id:N}"[..9] + $"-{Guid.NewGuid():N}"[..8];
                cq.Tags = (cq.Tags ?? new List<string>()).Append(Tag).Distinct().ToList();
                var q = await _seeder.UpsertQuestionAsync(cq, lesson.SubTopicId, lessonId, boardId, key, ct);
                q.Difficulty = original.Difficulty; q.MaxMarks = original.MaxMarks; q.Tier = original.Tier;
                await _db.SaveChangesAsync(ct);
                siblings.Add(new { q.Id, q.QuestionType, q.MaxMarks, q.Difficulty, q.QuestionText });
                made++; madeToday++;
            }
        }
        return made;
    }

    private async Task<ContentQuestion?> GenerateOneAsync(Question original, Lesson lesson, string context, IReadOnlyList<string> existingStems, CancellationToken ct)
    {
        var originalJson = JsonSerializer.Serialize(ToContent(original), JsonOpts);
        var system = "You write ORIGINAL GCSE practice questions for a UK tutoring platform (AQA). British English. Reply with ONE JSON object only: no prose, no markdown fences.\n" +
            "The JSON must use exactly this shape (same keys as the example question you are given): type, difficulty, marks, text, hint, explanation, examStyle, tier, and then the answer section that matches the type: " +
            "options[{text,correct,feedback?}] for MultipleChoice/TrueFalse (exactly one correct) and MultipleAnswer (one or more correct); " +
            "answers[{numeric,tolerance,unit?,marks}] for NumericalAnswer/FormulaCalculation (or {text,caseSensitive,marks} for symbolic answers); " +
            "answers[{text,blankIndex,marks}] plus metadata.blanksText containing ___ for FillInTheBlank; " +
            "markScheme[{criterion,marks,keywords:[[alternatives...]]}] for ShortAnswer/LongAnswer/ExamQuestion with 1-2 keyword groups per criterion and 2-5 lowercase alternatives per group; " +
            "pairs[{left,right}] for Matching; order[...] for Ordering. marks must equal the total of answer/mark-scheme marks.\n" +
            "Rules: same skill, same type, same difficulty and same marks as the example, but a genuinely different problem (new numbers, names, context and wording); never reuse the example's numbers or the listed existing questions; answers must be correct (work every calculation out); use $...$ for maths; include a helpful hint that does not give the answer and a worked explanation; content must be original and suitable for ages 13-16.";
        var user = $"Subject: {lesson.SubTopic.Topic.Subject.Name}. Topic: {lesson.SubTopic.Topic.Name}. Sub-topic: {lesson.SubTopic.Name}. Lesson: {lesson.Title}.\n\n" +
            (context.Length > 0 ? $"Lesson notes (stay within this content):\n{context}\n\n" : "") +
            $"Example question to mirror (JSON):\n{originalJson}\n\n" +
            "Existing questions on this step (do not duplicate any of these):\n- " + string.Join("\n- ", existingStems.Select(s => s.Length > 200 ? s[..200] : s)) +
            "\n\nWrite one new variant now as JSON.";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var completion = await _ai.CompleteAsync(new AiRequest(system, new[] { new AiChatMessage("user", user) }, 1800, "generation"), ct);
            if (completion.IsStub) return null;
            var cq = Parse(completion.Content);
            var errors = cq == null ? new List<string> { "not valid JSON" } : Validate(original, cq, existingStems);
            if (cq != null && errors.Count == 0) return cq;
            _logger.LogWarning("Generated variant rejected for question {QuestionId} (attempt {Attempt}): {Errors}", original.Id, attempt + 1, string.Join("; ", errors));
            user += "\n\nYour previous attempt was rejected because: " + string.Join("; ", errors) + ". Fix these and reply with the JSON only.";
        }
        return null;
    }

    internal static ContentQuestion? Parse(string content)
    {
        var text = content.Trim();
        var fence = Regex.Match(text, "```(?:json)?\\s*([\\s\\S]*?)```");
        if (fence.Success) text = fence.Groups[1].Value.Trim();
        var start = text.IndexOf('{'); var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try { return JsonSerializer.Deserialize<ContentQuestion>(text[start..(end + 1)], JsonOpts); } catch { return null; }
    }

    /// <summary>Structural checks so a bad generation can never reach a student: type, marks, answer section and no duplicates.</summary>
    internal static List<string> Validate(Question original, ContentQuestion cq, IReadOnlyList<string> existingStems)
    {
        var errors = new List<string>();
        if (!Enum.TryParse<QuestionType>(cq.Type, true, out var type) || type != original.QuestionType) errors.Add($"type must be {original.QuestionType}");
        if ((cq.Marks ?? 0) != original.MaxMarks) errors.Add($"marks must be {original.MaxMarks}");
        if (string.IsNullOrWhiteSpace(cq.Text) || cq.Text.Trim().Length < 15) errors.Add("text is missing or too short");
        else if (existingStems.Any(s => Normalise(s) == Normalise(cq.Text))) errors.Add("text duplicates an existing question");
        if (string.IsNullOrWhiteSpace(cq.Hint)) errors.Add("hint is missing");
        if (string.IsNullOrWhiteSpace(cq.Explanation)) errors.Add("explanation is missing");
        var marks = cq.Marks ?? 0;
        switch (type)
        {
            case QuestionType.MultipleChoice: case QuestionType.TrueFalse:
                if (cq.Options == null || cq.Options.Count < 2) errors.Add("needs at least two options");
                else if (cq.Options.Count(o => o.Correct == true) != 1) errors.Add("exactly one option must be correct");
                break;
            case QuestionType.MultipleAnswer:
                if (cq.Options == null || cq.Options.Count < 3) errors.Add("needs at least three options");
                else if (cq.Options.Count(o => o.Correct == true) < 1) errors.Add("at least one option must be correct");
                break;
            case QuestionType.NumericalAnswer: case QuestionType.FormulaCalculation: case QuestionType.Equation:
                if (cq.Answers == null || cq.Answers.Count == 0) errors.Add("needs answers");
                else { if (cq.Answers.Any(a => a.Numeric == null && string.IsNullOrWhiteSpace(a.Text))) errors.Add("each answer needs numeric or text"); if (cq.Answers.Sum(a => a.Marks ?? 1) != marks) errors.Add("answer marks must add up to marks"); }
                break;
            case QuestionType.FillInTheBlank:
                if (cq.Answers == null || cq.Answers.Count == 0) errors.Add("needs answers");
                else
                {
                    var blanks = cq.Metadata.HasValue && cq.Metadata.Value.ValueKind == JsonValueKind.Object && cq.Metadata.Value.TryGetProperty("blanksText", out var bt) ? (bt.GetString() ?? "") : "";
                    if (Regex.Matches(blanks, "___").Count != cq.Answers.Count) errors.Add("metadata.blanksText must contain one ___ per answer");
                    if (cq.Answers.Sum(a => a.Marks ?? 1) != marks) errors.Add("answer marks must add up to marks");
                }
                break;
            case QuestionType.ShortAnswer: case QuestionType.LongAnswer: case QuestionType.ExamQuestion:
                if (cq.MarkScheme == null || cq.MarkScheme.Count == 0) errors.Add("needs a markScheme");
                else
                {
                    if (cq.MarkScheme.Sum(c => c.Marks ?? 0) != marks) errors.Add("mark scheme marks must add up to marks");
                    foreach (var c in cq.MarkScheme)
                    {
                        if (c.Keywords == null || c.Keywords.Count < 1 || c.Keywords.Count > 2) errors.Add("each criterion needs 1-2 keyword groups");
                        else if (c.Keywords.Any(g => g.Count < 2 || g.Count > 5 || g.Any(string.IsNullOrWhiteSpace))) errors.Add("each keyword group needs 2-5 alternatives");
                    }
                }
                break;
            case QuestionType.Matching:
                if (cq.Pairs == null || cq.Pairs.Count < 3 || cq.Pairs.Any(p => string.IsNullOrWhiteSpace(p.Left) || string.IsNullOrWhiteSpace(p.Right))) errors.Add("needs at least three complete pairs");
                break;
            case QuestionType.Ordering:
                if (cq.Order == null || cq.Order.Count < 3) errors.Add("needs at least three items in order");
                break;
        }
        return errors;
    }

    private static string Normalise(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();

    /// <summary>Entity → content-file shape, used as the example in the prompt.</summary>
    internal static ContentQuestion ToContent(Question q)
    {
        var cq = new ContentQuestion
        {
            Type = q.QuestionType.ToString(), Difficulty = q.Difficulty, Marks = q.MaxMarks, Text = q.QuestionText, Hint = q.Hint, Explanation = q.Explanation,
            ExamStyle = q.IsExamStyle, Tier = q.Tier.ToString(),
        };
        var opts = q.Options.OrderBy(o => o.SortOrder).ToList();
        switch (q.QuestionType)
        {
            case QuestionType.Matching: cq.Pairs = opts.Select(o => new ContentPair { Left = o.Text, Right = o.MatchKey }).ToList(); break;
            case QuestionType.Ordering: cq.Order = opts.OrderBy(o => int.TryParse(o.MatchKey, out var i) ? i : o.SortOrder).Select(o => o.Text).ToList(); break;
            default: if (opts.Count > 0) cq.Options = opts.Select(o => new ContentOption { Text = o.Text, Correct = o.IsCorrect, Feedback = o.Feedback }).ToList(); break;
        }
        if (q.AcceptedAnswers.Count > 0)
            cq.Answers = q.AcceptedAnswers.Select(a => new ContentAnswer { Text = a.NumericValue.HasValue ? null : a.AnswerText, CaseSensitive = a.IsCaseSensitive, Numeric = a.NumericValue, Tolerance = a.NumericTolerance, Unit = a.Unit, BlankIndex = a.BlankIndex, Marks = a.Marks }).ToList();
        if (q.MarkScheme.Count > 0)
            cq.MarkScheme = q.MarkScheme.OrderBy(m => m.SortOrder).Select(m => new ContentCriterion { Criterion = m.CriterionText, Marks = m.Marks, Keywords = ParseKeywords(m.KeywordsJson) }).ToList();
        if (!string.IsNullOrWhiteSpace(q.MetadataJson)) { try { cq.Metadata = JsonDocument.Parse(q.MetadataJson).RootElement.Clone(); } catch { /* ignore */ } }
        return cq;
    }

    private static List<List<string>>? ParseKeywords(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<List<List<string>>>(json); } catch { return null; }
    }

    private async Task<T> SettingAsync<T>(string key, T fallback, CancellationToken ct) where T : struct
    {
        var raw = await _db.SystemSettings.Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        if (raw == null) return fallback;
        try { return (T)Convert.ChangeType(raw, typeof(T), System.Globalization.CultureInfo.InvariantCulture); } catch { return fallback; }
    }
}
