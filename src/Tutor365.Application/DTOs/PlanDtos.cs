namespace Tutor365.Application.DTOs;

public record CreateStudyPlanRequest(string Title, string? Notes, DateOnly? StartDate, DateOnly? EndDate, IReadOnlyList<StudyPlanItemInput> Items);
/// <summary>ItemType: Lesson (needs lessonId) | TopicReview (topicId) | TopicTest (topicId) | MockExam (subject only).</summary>
public record StudyPlanItemInput(Guid SubjectId, Guid? TopicId, Guid? LessonId, string? Priority, DateOnly? DueDate, int? QuestionCount, string? Notes, string? ItemType = null);
public record StudyPlanDto(Guid Id, Guid StudentId, string StudentName, string Title, string? Notes, DateOnly? StartDate, DateOnly? EndDate, string Status, bool IsSystemGenerated,
    Guid? CreatedByUserId, DateTime CreatedAt, IReadOnlyList<StudyPlanItemDto> Items, int CompletedItems, int TotalItems);

public record WeeklyReportDto(
    Guid StudentId, string StudentName, string YearGroup, DateOnly WeekStart, DateOnly WeekEnd,
    int StudyMinutes, int Sessions, int SessionsPlanned, decimal? AverageScore, int QuestionsAnswered, int QuestionsCorrect, int LessonsPassed, int CurrentStreakDays,
    IReadOnlyList<SubjectWeekDto> Subjects, IReadOnlyList<SubjectChangeDto> Improved, IReadOnlyList<SubjectChangeDto> NeedsAttention,
    IReadOnlyList<TopicProgressDto> WeakTopics, IReadOnlyList<RecommendationDto> RecommendedFocus, IReadOnlyList<WeeklyPointDto> Trend, string Summary);
public record SubjectWeekDto(Guid SubjectId, string SubjectName, string? ColourHex, int StudyMinutes, int Sessions, decimal? AverageScore, decimal? PreviousWeekScore, int TargetGrade, int? CurrentGrade);
public record SubjectChangeDto(Guid SubjectId, string SubjectName, decimal? From, decimal? To, string Note);
