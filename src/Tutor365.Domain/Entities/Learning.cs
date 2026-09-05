using Tutor365.Domain.Common;
using Tutor365.Domain.Enums;

namespace Tutor365.Domain.Entities;

public class StudySession : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public StudySessionType Type { get; set; } = StudySessionType.Lesson;
    public Guid? LessonId { get; set; }
    public Lesson? Lesson { get; set; }
    public Guid? AssessmentId { get; set; }
    public Assessment? Assessment { get; set; }
    public Guid SubjectId { get; set; }
    public Subject Subject { get; set; } = default!;
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid? SubTopicId { get; set; }
    public Guid? StudyPlanItemId { get; set; }
    public Guid? DailyStudySlotId { get; set; }
    public int AttemptNumber { get; set; } = 1;

    public StudySessionStatus Status { get; set; } = StudySessionStatus.NotStarted;
    public DateTime? StartedAt { get; set; }
    public DateTime? PausedAt { get; set; }
    public DateTime? ResumedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    /// <summary>Accumulated active seconds (excludes paused time).</summary>
    public int ElapsedSeconds { get; set; }
    public Guid? CurrentActivityId { get; set; }
    public Guid? CurrentQuestionId { get; set; }
    public decimal ProgressPercentage { get; set; }

    public int TotalQuestions { get; set; }
    public int QuestionsAnswered { get; set; }
    public int QuestionsCorrect { get; set; }
    public decimal CurrentScore { get; set; }
    public decimal MaxScore { get; set; }
    public decimal? ScorePercent { get; set; }
    public int PassThresholdPercent { get; set; } = 70;
    public bool? Passed { get; set; }
    /// <summary>Free-form JSON state the client can store (e.g. scroll position, draft answer).</summary>
    public string? ClientStateJson { get; set; }

    public ICollection<SessionActivity> Activities { get; set; } = new List<SessionActivity>();
    public ICollection<StudentAnswer> Answers { get; set; } = new List<StudentAnswer>();
}

public class SessionActivity : BaseEntity
{
    public Guid SessionId { get; set; }
    public StudySession Session { get; set; } = default!;
    public Guid? LessonActivityId { get; set; }
    public LessonActivity? LessonActivity { get; set; }
    public Guid? QuestionId { get; set; }
    public int SortOrder { get; set; }
    public SessionActivityStatus Status { get; set; } = SessionActivityStatus.Pending;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TimeSpentSeconds { get; set; }
}

public class StudentAnswer : BaseEntity
{
    public Guid SessionId { get; set; }
    public StudySession Session { get; set; } = default!;
    public Guid StudentId { get; set; }
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = default!;
    public Guid? SessionActivityId { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public string? AnswerText { get; set; }
    /// <summary>Structured answer for non-text types (selected option ids, ordering, matching pairs).</summary>
    public string? AnswerJson { get; set; }
    public bool IsCorrect { get; set; }
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    public int TimeSpentSeconds { get; set; }
    public string? Feedback { get; set; }
    /// <summary>JSON array of mark-scheme criteria the answer did not meet.</summary>
    public string? MissingCriteriaJson { get; set; }
    public MarkingSource MarkedBy { get; set; } = MarkingSource.Rule;
    public bool HintUsed { get; set; }
    public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;
    public bool IsReviewed { get; set; }
}

public class AssessmentResult : BaseEntity
{
    public Guid AssessmentId { get; set; }
    public Assessment Assessment { get; set; } = default!;
    public Guid StudentId { get; set; }
    public Guid? SessionId { get; set; }
    public decimal Score { get; set; }
    public decimal MaxScore { get; set; }
    public decimal Percentage { get; set; }
    public int? EstimatedGrade { get; set; }
    public bool Passed { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class StudentTopicProgress : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public Guid TopicId { get; set; }
    public Topic Topic { get; set; } = default!;
    /// <summary>0.0 - 1.0</summary>
    public decimal MasteryScore { get; set; }
    public int QuestionsAttempted { get; set; }
    public int QuestionsCorrect { get; set; }
    public int LessonsCompleted { get; set; }
    public int LessonsTotal { get; set; }
    public DateTime? LastAttemptedAt { get; set; }
    public DateTime? LastReviewedAt { get; set; }
    public DateTime? NextReviewAt { get; set; }
    public int ReviewCount { get; set; }
    /// <summary>Current spaced-repetition interval index (0 = day1, 1 = day2, 2 = day5, ...).</summary>
    public int ReviewStage { get; set; }
    public int DifficultyLevel { get; set; } = 3;
    /// <summary>0.0 - 1.0 confidence derived from consistency of recent answers.</summary>
    public decimal ConfidenceLevel { get; set; }
    public MasteryStatus Status { get; set; } = MasteryStatus.NotStarted;
    public int StudyTimeMinutes { get; set; }
}

public class StudentSubjectProgress : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public Guid SubjectId { get; set; }
    public Subject Subject { get; set; } = default!;
    public decimal AverageScore { get; set; }
    public decimal MasteryScore { get; set; }
    public int QuestionsAttempted { get; set; }
    public int QuestionsCorrect { get; set; }
    public int StudyTimeMinutes { get; set; }
    public int SessionsCompleted { get; set; }
    public int LessonsCompleted { get; set; }
    public DateTime? LastStudiedAt { get; set; }
    public int? CurrentGrade { get; set; }
    public int TargetGrade { get; set; } = 5;
}

public class StudentLessonProgress : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public Guid LessonId { get; set; }
    public Lesson Lesson { get; set; } = default!;
    public LessonProgressStatus Status { get; set; } = LessonProgressStatus.Available;
    public int Attempts { get; set; }
    public decimal? BestScorePercent { get; set; }
    public decimal? LastScorePercent { get; set; }
    public bool Passed { get; set; }
    public DateTime? FirstStartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? PassedAt { get; set; }
    public Guid? LastSessionId { get; set; }
}

public class StudyPlan : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;
    /// <summary>Null when generated by the platform itself; otherwise the parent/admin who created it.</summary>
    public Guid? CreatedByUserId { get; set; }
    public bool IsSystemGenerated { get; set; }
    public string Title { get; set; } = default!;
    public string? Notes { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public StudyPlanStatus Status { get; set; } = StudyPlanStatus.Active;
    public ICollection<StudyPlanItem> Items { get; set; } = new List<StudyPlanItem>();
}

public class StudyPlanItem : BaseEntity
{
    public Guid StudyPlanId { get; set; }
    public StudyPlan StudyPlan { get; set; } = default!;
    public Guid SubjectId { get; set; }
    public Subject Subject { get; set; } = default!;
    public Guid? TopicId { get; set; }
    public Guid? LessonId { get; set; }
    public Lesson? Lesson { get; set; }
    public Guid? AssessmentId { get; set; }
    public Assessment? Assessment { get; set; }
    public StudyPlanPriority Priority { get; set; } = StudyPlanPriority.Normal;
    public DateOnly? DueDate { get; set; }
    public int? QuestionCount { get; set; }
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public StudyPlanItemStatus Status { get; set; } = StudyPlanItemStatus.Pending;
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedSessionId { get; set; }
}

/// <summary>A planned 45-minute block on a given day, generated from the student's schedule and the recommendation engine.</summary>
public class DailyStudySlot : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public DateOnly Date { get; set; }
    public int SlotNumber { get; set; }
    public int DurationMinutes { get; set; } = 45;
    public Guid SubjectId { get; set; }
    public Subject Subject { get; set; } = default!;
    public Guid? TopicId { get; set; }
    public Guid? LessonId { get; set; }
    public Lesson? Lesson { get; set; }
    public StudySessionType SessionType { get; set; } = StudySessionType.Lesson;
    public string? Reason { get; set; }
    public DailySlotStatus Status { get; set; } = DailySlotStatus.Scheduled;
    public Guid? SessionId { get; set; }
    public Guid? StudyPlanItemId { get; set; }
}

public class AIConversation : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public Guid? SessionId { get; set; }
    public Guid? LessonId { get; set; }
    public Guid? SubjectId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? QuestionId { get; set; }
    public int TotalTokens { get; set; }
    public ICollection<AIConversationMessage> Messages { get; set; } = new List<AIConversationMessage>();
}

public class AIConversationMessage : BaseEntity
{
    public Guid ConversationId { get; set; }
    public AIConversation Conversation { get; set; } = default!;
    public AiMessageRole Role { get; set; }
    public string Message { get; set; } = default!;
    public string? Intent { get; set; }
    public int? Tokens { get; set; }
}
