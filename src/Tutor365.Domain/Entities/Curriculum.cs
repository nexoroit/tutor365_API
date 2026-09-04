using Tutor365.Domain.Common;
using Tutor365.Domain.Enums;

namespace Tutor365.Domain.Entities;

public class ExamBoard : BaseEntity
{
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Website { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Qualification> Qualifications { get; set; } = new List<Qualification>();
}

public class YearGroup : BaseEntity
{
    public int Number { get; set; }
    public string Name { get; set; } = default!;
    public KeyStage KeyStage { get; set; }
}

public class Subject : BaseEntity
{
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? ColourHex { get; set; }
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Qualification : BaseEntity
{
    public Guid ExamBoardId { get; set; }
    public ExamBoard ExamBoard { get; set; } = default!;
    public Guid SubjectId { get; set; }
    public Subject Subject { get; set; } = default!;
    public string Code { get; set; } = default!;   // e.g. 8461
    public string Name { get; set; } = default!;   // e.g. GCSE Biology
    public bool HasTiers { get; set; }
    public string? SpecificationUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<Topic> Topics { get; set; } = new List<Topic>();
}

public class Topic : BaseEntity
{
    public Guid QualificationId { get; set; }
    public Qualification Qualification { get; set; } = default!;
    public Guid SubjectId { get; set; }
    public Subject Subject { get; set; } = default!;
    /// <summary>Year in which this topic is typically first taught (drives per-year study plans).</summary>
    public Guid? YearGroupId { get; set; }
    public YearGroup? YearGroup { get; set; }
    public string Code { get; set; } = default!;     // e.g. BIO-01
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? SpecificationReference { get; set; } // e.g. 4.1
    public int SortOrder { get; set; }
    /// <summary>Relative exam weighting 1-10, used by the recommendation engine.</summary>
    public int ExamWeight { get; set; } = 5;
    public Tier Tier { get; set; } = Tier.NotApplicable;
    public ContentStatus Status { get; set; } = ContentStatus.Published;
    public ICollection<SubTopic> SubTopics { get; set; } = new List<SubTopic>();
}

public class SubTopic : BaseEntity
{
    public Guid TopicId { get; set; }
    public Topic Topic { get; set; } = default!;
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? SpecificationReference { get; set; }
    public int SortOrder { get; set; }
    public Tier Tier { get; set; } = Tier.NotApplicable;
    public ContentStatus Status { get; set; } = ContentStatus.Published;
    public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
    public ICollection<CurriculumMapping> Mappings { get; set; } = new List<CurriculumMapping>();
}

/// <summary>Maps a sub-topic to the exam-board spec and the reference book section (e.g. CGP). Never stores book text.</summary>
public class CurriculumMapping : BaseEntity
{
    public Guid SubTopicId { get; set; }
    public SubTopic SubTopic { get; set; } = default!;
    public Guid ExamBoardId { get; set; }
    public ExamBoard ExamBoard { get; set; } = default!;
    public string? SpecificationReference { get; set; }
    public string? ReferenceBookTitle { get; set; }
    public string? ReferenceBookIsbn { get; set; }
    public string? ReferenceBookSection { get; set; }
    public string? Notes { get; set; }
}

public class Lesson : BaseEntity
{
    public Guid SubTopicId { get; set; }
    public SubTopic SubTopic { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Summary { get; set; }
    /// <summary>JSON array of learning objectives.</summary>
    public string? ObjectivesJson { get; set; }
    public int EstimatedMinutes { get; set; } = 45;
    public int Difficulty { get; set; } = 3;
    public int SortOrder { get; set; }
    public Tier Tier { get; set; } = Tier.NotApplicable;
    public ContentStatus Status { get; set; } = ContentStatus.Published;
    public int Version { get; set; } = 1;
    public Guid? CreatedByUserId { get; set; }
    public ICollection<LessonActivity> Activities { get; set; } = new List<LessonActivity>();
}

public class LessonActivity : BaseEntity
{
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = default!;
    public int SortOrder { get; set; }
    public LessonActivityType Type { get; set; }
    public string Title { get; set; } = default!;
    /// <summary>Markdown content for explanation/example/summary activities.</summary>
    public string? ContentMarkdown { get; set; }
    public Guid? QuestionId { get; set; }
    public Question? Question { get; set; }
    public int EstimatedMinutes { get; set; } = 3;
    /// <summary>If true the activity's question must be passed before continuing.</summary>
    public bool IsCheckpoint { get; set; }
}

public class Question : BaseEntity
{
    public Guid SubTopicId { get; set; }
    public SubTopic SubTopic { get; set; } = default!;
    public Guid? LessonId { get; set; }
    public QuestionType QuestionType { get; set; }
    /// <summary>1 (easy) to 5 (hard).</summary>
    public int Difficulty { get; set; } = 3;
    public string QuestionText { get; set; } = default!;
    public string? ImageUrl { get; set; }
    public int MaxMarks { get; set; } = 1;
    public bool IsExamStyle { get; set; }
    public Guid? ExamBoardId { get; set; }
    public Tier Tier { get; set; } = Tier.NotApplicable;
    public int? TimeLimitSeconds { get; set; }
    public string? Hint { get; set; }
    /// <summary>Worked solution / explanation shown after answering.</summary>
    public string? Explanation { get; set; }
    /// <summary>Type-specific structured data (matching pairs, ordering items, blanks, diagram labels, units).</summary>
    public string? MetadataJson { get; set; }
    public ContentStatus Status { get; set; } = ContentStatus.Published;
    public string? Tags { get; set; }
    public ICollection<QuestionOption> Options { get; set; } = new List<QuestionOption>();
    public ICollection<QuestionAnswer> AcceptedAnswers { get; set; } = new List<QuestionAnswer>();
    public ICollection<MarkScheme> MarkScheme { get; set; } = new List<MarkScheme>();
}

public class QuestionOption : BaseEntity
{
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = default!;
    public string Text { get; set; } = default!;
    public bool IsCorrect { get; set; }
    public int SortOrder { get; set; }
    /// <summary>For matching questions: the key this option pairs with. For ordering: the correct position.</summary>
    public string? MatchKey { get; set; }
    public string? Feedback { get; set; }
}

public class QuestionAnswer : BaseEntity
{
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = default!;
    public string AnswerText { get; set; } = default!;
    public bool IsCaseSensitive { get; set; }
    public decimal? NumericValue { get; set; }
    public decimal? NumericTolerance { get; set; }
    public string? Unit { get; set; }
    /// <summary>For fill-in-the-blank: which blank (0-based) this answer belongs to.</summary>
    public int? BlankIndex { get; set; }
    public int Marks { get; set; } = 1;
}

public class MarkScheme : BaseEntity
{
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = default!;
    public int SortOrder { get; set; }
    public string CriterionText { get; set; } = default!;
    public int Marks { get; set; } = 1;
    /// <summary>JSON array of keyword groups; any keyword in a group matching awards the marks.</summary>
    public string? KeywordsJson { get; set; }
}

public class Assessment : BaseEntity
{
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public AssessmentType Type { get; set; }
    public Guid SubjectId { get; set; }
    public Subject Subject { get; set; } = default!;
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid? SubTopicId { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public int TotalMarks { get; set; }
    public int PassMarkPercent { get; set; } = 60;
    public ContentStatus Status { get; set; } = ContentStatus.Published;
    public Guid? CreatedByUserId { get; set; }
    public ICollection<AssessmentQuestion> Questions { get; set; } = new List<AssessmentQuestion>();
}

public class AssessmentQuestion
{
    public Guid AssessmentId { get; set; }
    public Assessment Assessment { get; set; } = default!;
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = default!;
    public int SortOrder { get; set; }
    public int Marks { get; set; }
}
