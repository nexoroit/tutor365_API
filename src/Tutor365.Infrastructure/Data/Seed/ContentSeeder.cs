using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tutor365.Domain.Common;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;

namespace Tutor365.Infrastructure.Data.Seed;

/// <summary>Imports lesson content JSON files (see docs/CONTENT_FORMAT.md). Idempotent: keys map to deterministic GUIDs.</summary>
public class ContentSeeder
{
    private readonly AppDbContext _db;
    private readonly ILogger<ContentSeeder> _logger;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public ContentSeeder(AppDbContext db, ILogger<ContentSeeder> logger) { _db = db; _logger = logger; }

    public record ImportResult(int Files, int Lessons, int Questions, List<string> Errors);

    public async Task<ImportResult> ImportDirectoryAsync(string rootPath, CancellationToken ct = default)
    {
        var result = new ImportResult(0, 0, 0, new List<string>());
        if (!Directory.Exists(rootPath))
        {
            _logger.LogWarning("Content directory {Path} not found; skipping content import.", rootPath);
            return result;
        }
        var files = Directory.GetFiles(rootPath, "*.json", SearchOption.AllDirectories).OrderBy(f => f).ToList();
        var lessons = 0; var questions = 0;
        foreach (var file in files)
        {
            try
            {
                var (l, q) = await ImportFileAsync(file, ct);
                lessons += l; questions += q;
            }
            catch (Exception ex)
            {
                var msg = $"{Path.GetFileName(file)}: {ex.Message}";
                result.Errors.Add(msg);
                _logger.LogError(ex, "Content import failed for {File}", file);
                _db.ChangeTracker.Clear();
            }
        }
        _logger.LogInformation("Content import: {Files} files, {Lessons} lessons, {Questions} questions, {Errors} errors", files.Count, lessons, questions, result.Errors.Count);
        return result with { Files = files.Count, Lessons = lessons, Questions = questions };
    }

    public async Task<(int Lessons, int Questions)> ImportFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var file = await JsonSerializer.DeserializeAsync<ContentFile>(stream, Json, ct) ?? throw new InvalidOperationException("Empty file");
        return await ImportAsync(file, ct);
    }

    public async Task<(int Lessons, int Questions)> ImportAsync(ContentFile file, CancellationToken ct)
    {
        var board = string.IsNullOrWhiteSpace(file.Board) ? "AQA" : file.Board;
        var subTopicId = DeterministicGuid.Create($"subtopic:{board}:{file.SubTopicCode}");
        var subTopic = await _db.SubTopics.FirstOrDefaultAsync(s => s.Id == subTopicId, ct)
            ?? throw new InvalidOperationException($"Sub-topic {file.SubTopicCode} ({board}) does not exist");
        var boardId = DeterministicGuid.Create($"examboard:{board}");

        var lessonCount = 0; var questionCount = 0;
        var order = 0;
        foreach (var l in file.Lessons)
        {
            order++;
            if (string.IsNullOrWhiteSpace(l.Key)) throw new InvalidOperationException("Lesson key missing");
            var lessonId = DeterministicGuid.Create($"lesson:{l.Key}");
            var lesson = await _db.Lessons.Include(x => x.Activities).FirstOrDefaultAsync(x => x.Id == lessonId, ct);
            if (lesson == null)
            {
                lesson = new Lesson { Id = lessonId, SubTopicId = subTopicId };
                _db.Lessons.Add(lesson);
            }
            else lesson.SubTopicId = subTopicId;

            lesson.Title = l.Title ?? l.Key;
            lesson.Summary = l.Summary;
            lesson.ObjectivesJson = l.Objectives == null ? null : JsonSerializer.Serialize(l.Objectives);
            lesson.EstimatedMinutes = l.EstimatedMinutes ?? 45;
            lesson.Difficulty = Math.Clamp(l.Difficulty ?? 3, 1, 5);
            lesson.SortOrder = l.SortOrder ?? order;
            lesson.Tier = ParseTier(l.Tier);
            lesson.Status = ContentStatus.Published;
            lesson.Version++;

            // Activities
            var seenActivityIds = new HashSet<Guid>();
            var actOrder = 0;
            foreach (var a in l.Activities ?? new())
            {
                actOrder++;
                var actId = DeterministicGuid.Create($"activity:{l.Key}:{actOrder}");
                seenActivityIds.Add(actId);
                var activity = lesson.Activities.FirstOrDefault(x => x.Id == actId);
                if (activity == null)
                {
                    activity = new LessonActivity { Id = actId, LessonId = lessonId };
                    _db.LessonActivities.Add(activity);
                    lesson.Activities.Add(activity);
                }
                activity.SortOrder = actOrder;
                activity.Type = Enum.TryParse<LessonActivityType>(a.Type, true, out var t) ? t : LessonActivityType.Explanation;
                activity.Title = a.Title ?? activity.Type.ToString();
                activity.ContentMarkdown = a.Content;
                activity.EstimatedMinutes = a.Minutes ?? (activity.Type == LessonActivityType.Question ? 3 : 4);
                activity.IsCheckpoint = a.Checkpoint ?? false;

                if (a.Question != null)
                {
                    activity.Type = LessonActivityType.Question;
                    var q = await UpsertQuestionAsync(a.Question, subTopicId, lessonId, boardId, $"{l.Key}:A{actOrder}", ct);
                    activity.QuestionId = q.Id;
                    questionCount++;
                }
                else activity.QuestionId = null;
            }
            foreach (var stale in lesson.Activities.Where(x => !seenActivityIds.Contains(x.Id)).ToList())
                _db.LessonActivities.Remove(stale);

            // Practice pool
            var pOrder = 0;
            foreach (var pq in l.PracticeQuestions ?? new())
            {
                pOrder++;
                await UpsertQuestionAsync(pq, subTopicId, lessonId, boardId, $"{l.Key}:P{pOrder}", ct);
                questionCount++;
            }
            lessonCount++;
        }
        await _db.SaveChangesAsync(ct);
        return (lessonCount, questionCount);
    }

    private async Task<Question> UpsertQuestionAsync(ContentQuestion cq, Guid subTopicId, Guid lessonId, Guid boardId, string fallbackKey, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(cq.Key) ? fallbackKey : cq.Key;
        var id = DeterministicGuid.Create($"question:{key}");
        var q = await _db.Questions.Include(x => x.Options).Include(x => x.AcceptedAnswers).Include(x => x.MarkScheme)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q == null)
        {
            q = new Question { Id = id };
            _db.Questions.Add(q);
        }
        q.SubTopicId = subTopicId;
        q.LessonId = lessonId;
        q.QuestionType = Enum.TryParse<QuestionType>(cq.Type, true, out var qt) ? qt : QuestionType.ShortAnswer;
        q.Difficulty = Math.Clamp(cq.Difficulty ?? 3, 1, 5);
        q.QuestionText = cq.Text ?? "";
        q.Hint = cq.Hint;
        q.Explanation = cq.Explanation;
        q.IsExamStyle = cq.ExamStyle ?? q.QuestionType == QuestionType.ExamQuestion;
        q.ExamBoardId = boardId;
        q.Tier = ParseTier(cq.Tier);
        q.Status = ContentStatus.Published;
        q.Tags = cq.Tags == null ? null : string.Join(",", cq.Tags);

        var metadata = cq.Metadata.HasValue && cq.Metadata.Value.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(cq.Metadata.Value.GetRawText()) ?? new()
            : new Dictionary<string, object?>();

        // Options (choice, matching, ordering)
        _db.QuestionOptions.RemoveRange(q.Options);
        q.Options.Clear();
        var so = 0;
        if (cq.Options != null)
        {
            foreach (var o in cq.Options)
                q.Options.Add(new QuestionOption { Id = DeterministicGuid.Create($"opt:{key}:{so}"), QuestionId = id, Text = o.Text ?? "", IsCorrect = o.Correct ?? false, Feedback = o.Feedback, SortOrder = so++ });
        }
        if (cq.Pairs != null)
        {
            foreach (var p in cq.Pairs)
                q.Options.Add(new QuestionOption { Id = DeterministicGuid.Create($"opt:{key}:{so}"), QuestionId = id, Text = p.Left ?? "", MatchKey = p.Right, IsCorrect = true, SortOrder = so++ });
            metadata["matchTargets"] = cq.Pairs.Select(p => p.Right).Distinct().OrderBy(_ => Guid.NewGuid()).ToList();
        }
        if (cq.Order != null)
        {
            var idx = 0;
            foreach (var item in cq.Order)
                q.Options.Add(new QuestionOption { Id = DeterministicGuid.Create($"opt:{key}:{so}"), QuestionId = id, Text = item, MatchKey = (idx++).ToString(), IsCorrect = true, SortOrder = so++ });
        }

        // Accepted answers
        _db.QuestionAnswers.RemoveRange(q.AcceptedAnswers);
        q.AcceptedAnswers.Clear();
        var ai = 0;
        foreach (var a in cq.Answers ?? new())
        {
            q.AcceptedAnswers.Add(new QuestionAnswer
            {
                Id = DeterministicGuid.Create($"ans:{key}:{ai++}"), QuestionId = id,
                AnswerText = a.Text ?? (a.Numeric?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                IsCaseSensitive = a.CaseSensitive ?? false, NumericValue = a.Numeric, NumericTolerance = a.Tolerance,
                Unit = a.Unit, BlankIndex = a.BlankIndex, Marks = a.Marks ?? 1
            });
        }

        // Mark scheme
        _db.MarkSchemes.RemoveRange(q.MarkScheme);
        q.MarkScheme.Clear();
        var mi = 0;
        foreach (var m in cq.MarkScheme ?? new())
        {
            q.MarkScheme.Add(new MarkScheme
            {
                Id = DeterministicGuid.Create($"ms:{key}:{mi}"), QuestionId = id, SortOrder = mi++,
                CriterionText = m.Criterion ?? "", Marks = m.Marks ?? 1,
                KeywordsJson = m.Keywords == null ? null : JsonSerializer.Serialize(m.Keywords)
            });
        }

        // Marks: explicit, else derived
        var derived = q.QuestionType switch
        {
            QuestionType.ShortAnswer or QuestionType.LongAnswer or QuestionType.ExamQuestion => q.MarkScheme.Sum(m => m.Marks),
            QuestionType.FillInTheBlank => q.AcceptedAnswers.GroupBy(a => a.BlankIndex ?? 0).Sum(g => g.Max(a => a.Marks)),
            QuestionType.Matching or QuestionType.DragAndDrop or QuestionType.DiagramLabelling => q.Options.Count,
            QuestionType.MultipleAnswer => Math.Max(1, q.Options.Count(o => o.IsCorrect)),
            _ => q.AcceptedAnswers.Count > 0 ? q.AcceptedAnswers.Max(a => a.Marks) : 1
        };
        q.MaxMarks = cq.Marks ?? Math.Max(1, derived);

        q.MetadataJson = metadata.Count == 0 ? null : JsonSerializer.Serialize(metadata);
        return q;
    }

    private static Tier ParseTier(string? tier) => Enum.TryParse<Tier>(tier, true, out var t) ? t : Tier.NotApplicable;

    // ---- JSON contract ----
    public class ContentFile
    {
        public string SubTopicCode { get; set; } = default!;
        public string? Board { get; set; }
        public List<ContentLesson> Lessons { get; set; } = new();
    }
    public class ContentLesson
    {
        public string Key { get; set; } = default!;
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public List<string>? Objectives { get; set; }
        public int? EstimatedMinutes { get; set; }
        public int? Difficulty { get; set; }
        public int? SortOrder { get; set; }
        public string? Tier { get; set; }
        public List<ContentActivity>? Activities { get; set; }
        public List<ContentQuestion>? PracticeQuestions { get; set; }
    }
    public class ContentActivity
    {
        public string? Type { get; set; }
        public string? Title { get; set; }
        public string? Content { get; set; }
        public int? Minutes { get; set; }
        public bool? Checkpoint { get; set; }
        public ContentQuestion? Question { get; set; }
    }
    public class ContentQuestion
    {
        public string? Key { get; set; }
        public string? Type { get; set; }
        public int? Difficulty { get; set; }
        public int? Marks { get; set; }
        public string? Text { get; set; }
        public string? Hint { get; set; }
        public string? Explanation { get; set; }
        public bool? ExamStyle { get; set; }
        public string? Tier { get; set; }
        public List<string>? Tags { get; set; }
        public List<ContentOption>? Options { get; set; }
        public List<ContentAnswer>? Answers { get; set; }
        public List<ContentCriterion>? MarkScheme { get; set; }
        public List<ContentPair>? Pairs { get; set; }
        public List<string>? Order { get; set; }
        public JsonElement? Metadata { get; set; }
    }
    public class ContentOption { public string? Text { get; set; } public bool? Correct { get; set; } public string? Feedback { get; set; } }
    public class ContentAnswer
    {
        public string? Text { get; set; } public bool? CaseSensitive { get; set; } public decimal? Numeric { get; set; }
        public decimal? Tolerance { get; set; } public string? Unit { get; set; } public int? BlankIndex { get; set; } public int? Marks { get; set; }
    }
    public class ContentCriterion { public string? Criterion { get; set; } public int? Marks { get; set; } public List<List<string>>? Keywords { get; set; } }
    public class ContentPair { public string? Left { get; set; } public string? Right { get; set; } }
}
