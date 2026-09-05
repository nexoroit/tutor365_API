namespace Tutor365.Application.DTOs;

public record SubjectProgressDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    string? ColourHex,
    decimal AverageScore,
    decimal MasteryPercent,
    int QuestionsAttempted,
    int QuestionsCorrect,
    int StudyTimeMinutes,
    int SessionsCompleted,
    int LessonsCompleted,
    int LessonsTotal,
    DateTime? LastStudiedAt,
    int? CurrentGrade,
    int TargetGrade,
    string Status,
    string Trend);

public record TopicProgressDto(
    Guid TopicId,
    string TopicCode,
    string TopicName,
    Guid SubjectId,
    string SubjectName,
    decimal MasteryPercent,
    string Status,
    int QuestionsAttempted,
    int QuestionsCorrect,
    int LessonsCompleted,
    int LessonsTotal,
    DateTime? LastAttemptedAt,
    DateTime? NextReviewAt,
    bool ReviewDue,
    int ReviewCount,
    decimal ConfidencePercent,
    /// <summary>All lessons passed and the topic test not yet passed.</summary>
    bool ReadyForTest = false,
    decimal? LastAssessmentPercent = null,
    int AssessmentAttempts = 0,
    DateTime? AssessmentPassedAt = null);

public record LessonProgressDto(
    Guid LessonId,
    string Title,
    Guid SubTopicId,
    string SubTopicName,
    Guid TopicId,
    int SortOrder,
    int EstimatedMinutes,
    int Difficulty,
    string Tier,
    string Status,
    int Attempts,
    decimal? BestScorePercent,
    decimal? LastScorePercent,
    bool Passed,
    DateTime? CompletedAt,
    Guid? ActiveSessionId);

public record ProgressOverviewDto(
    decimal OverallPercent,
    decimal OverallMasteryPercent,
    int? EstimatedGrade,
    int TargetGrade,
    int QuestionsAttempted,
    int QuestionsCorrect,
    int TotalStudyMinutes,
    int SessionsCompleted,
    int LessonsCompleted,
    int CurrentStreakDays,
    int LongestStreakDays,
    IReadOnlyList<SubjectProgressDto> Subjects,
    IReadOnlyList<TopicProgressDto> WeakTopics,
    IReadOnlyList<TopicProgressDto> StrongTopics,
    IReadOnlyList<WeeklyPointDto> WeeklyTrend);

public record WeeklyPointDto(DateOnly WeekStart, int StudyMinutes, int Sessions, decimal? AverageScore);

public record RecommendationDto(
    Guid SubjectId,
    string SubjectName,
    string? SubjectColour,
    Guid? TopicId,
    string? TopicName,
    Guid? LessonId,
    string? LessonTitle,
    string SessionType,
    string Reason,
    int Priority,
    int EstimatedMinutes);

public record DailySlotDto(
    Guid Id,
    DateOnly Date,
    int SlotNumber,
    int DurationMinutes,
    Guid SubjectId,
    string SubjectName,
    string? SubjectColour,
    Guid? TopicId,
    string? TopicName,
    Guid? LessonId,
    string? LessonTitle,
    string SessionType,
    string? Reason,
    string Status,
    Guid? SessionId);

public record TodayPlanDto(
    DateOnly Date,
    int SessionsPlanned,
    int SessionsCompleted,
    int MinutesPlanned,
    int MinutesCompleted,
    bool IsRestDay,
    IReadOnlyList<DailySlotDto> Slots);

public record StudentDashboardDto(
    string Greeting,
    string FirstName,
    string YearGroup,
    int TargetGrade,
    int? EstimatedGrade,
    decimal OverallPercent,
    int CurrentStreakDays,
    int WeeklyStudyMinutes,
    int WeeklyGoalMinutes,
    TodayPlanDto Today,
    SessionSummaryDto? ContinueSession,
    RecommendationDto? Recommended,
    IReadOnlyList<SubjectProgressDto> Subjects,
    IReadOnlyList<SessionSummaryDto> RecentResults,
    IReadOnlyList<TopicProgressDto> WeakAreas,
    IReadOnlyList<StudyPlanItemDto> AssignedWork,
    int UnreadNotifications);

public record StudyPlanItemDto(
    Guid Id,
    Guid StudyPlanId,
    string PlanTitle,
    Guid SubjectId,
    string SubjectName,
    Guid? TopicId,
    string? TopicName,
    Guid? LessonId,
    string? LessonTitle,
    Guid? AssessmentId,
    string ItemType,
    string Priority,
    DateOnly? DueDate,
    int? QuestionCount,
    string? Notes,
    string Status,
    DateTime? CompletedAt);
