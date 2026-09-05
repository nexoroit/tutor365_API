using System.Text.Json;

namespace Tutor365.Application.DTOs;

// ---------- requests ----------

public record StartSessionRequest(
    Guid? LessonId,
    Guid? TopicId,
    Guid? SubjectId,
    Guid? DailyStudySlotId,
    Guid? StudyPlanItemId,
    /// <summary>"Lesson" (default), "Review" (topic review), "Practice", "Assessment" (timed topic test, needs topicId) or "Mock" (timed subject mock, needs subjectId).</summary>
    string? Type,
    int? QuestionCount,
    bool ForceNew = false);

public record SubmitAnswerRequest(
    Guid QuestionId,
    string? AnswerText,
    JsonElement? AnswerJson,
    int TimeSpentSeconds = 0,
    bool HintUsed = false);

public record SessionHeartbeatRequest(int? ElapsedSeconds, Guid? CurrentActivityId, JsonElement? ClientState);

public record NavigateRequest(Guid? ActivityId, string? Direction);

// ---------- rendered question (never includes correct flags/answers) ----------

public record QuestionOptionDto(Guid Id, string Text, int SortOrder);

public record QuestionDto(
    Guid Id,
    string QuestionType,
    string QuestionText,
    int MaxMarks,
    int Difficulty,
    bool IsExamStyle,
    string? ImageUrl,
    int? TimeLimitSeconds,
    bool HasHint,
    IReadOnlyList<QuestionOptionDto> Options,
    IReadOnlyList<string>? MatchTargets,
    string? BlanksText,
    int BlankCount,
    JsonElement? Metadata);

public record AnswerResultDto(
    Guid AnswerId,
    Guid QuestionId,
    bool Correct,
    decimal Score,
    decimal MaxScore,
    string Feedback,
    IReadOnlyList<string> MissingCriteria,
    string? Explanation,
    string? CorrectAnswer,
    string MarkedBy,
    int AttemptNumber,
    string NextAction,
    SessionProgressDto Session,
    /// <summary>True in assessments: the answer is recorded but correctness and feedback are withheld until the test is submitted.</summary>
    bool FeedbackDeferred = false);

// ---------- session state ----------

public record SessionActivityDto(
    Guid Id,
    int SortOrder,
    string Type,
    string Title,
    string Status,
    string? ContentMarkdown,
    int EstimatedMinutes,
    bool IsCheckpoint,
    QuestionDto? Question,
    AnswerSummaryDto? Answer);

public record AnswerSummaryDto(Guid AnswerId, bool Correct, decimal Score, decimal MaxScore, string? Feedback, string? AnswerText, JsonElement? AnswerJson, int AttemptNumber);

public record SessionProgressDto(
    Guid SessionId,
    string Status,
    decimal ProgressPercentage,
    int ElapsedSeconds,
    int TotalActivities,
    int CompletedActivities,
    int TotalQuestions,
    int QuestionsAnswered,
    int QuestionsCorrect,
    decimal CurrentScore,
    decimal MaxScore,
    Guid? CurrentActivityId);

public record StudySessionDto(
    Guid Id,
    string Type,
    string Status,
    int AttemptNumber,
    Guid StudentId,
    Guid SubjectId,
    string SubjectName,
    string? SubjectColour,
    Guid? TopicId,
    string? TopicName,
    Guid? SubTopicId,
    string? SubTopicName,
    Guid? LessonId,
    string? LessonTitle,
    int EstimatedMinutes,
    int PassThresholdPercent,
    DateTime? StartedAt,
    DateTime? PausedAt,
    DateTime? CompletedAt,
    DateTime? LastActivityAt,
    SessionProgressDto Progress,
    SessionActivityDto? CurrentActivity,
    IReadOnlyList<SessionActivityDto> Activities,
    JsonElement? ClientState,
    /// <summary>Assessment/Mock: timed, no hints or AI help, feedback after submission.</summary>
    bool IsAssessment = false,
    int? TimeLimitMinutes = null,
    /// <summary>Seconds left before auto-submit (assessments only).</summary>
    int? SecondsRemaining = null);

public record SessionSummaryDto(
    Guid Id,
    string Type,
    string Status,
    Guid SubjectId,
    string SubjectName,
    string? SubjectColour,
    Guid? TopicId,
    string? TopicName,
    Guid? LessonId,
    string? LessonTitle,
    int AttemptNumber,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int ElapsedSeconds,
    decimal ProgressPercentage,
    decimal? ScorePercent,
    int QuestionsAnswered,
    int QuestionsCorrect,
    bool? Passed);

// ---------- results ----------

public record SessionResultDto(
    SessionSummaryDto Session,
    decimal Score,
    decimal MaxScore,
    decimal ScorePercent,
    bool Passed,
    int PassThresholdPercent,
    int AttemptNumber,
    bool CanRetry,
    bool NextLessonUnlocked,
    Guid? NextLessonId,
    string? NextLessonTitle,
    IReadOnlyList<PerformanceBandDto> Performance,
    IReadOnlyList<MistakeDto> Mistakes,
    string Message,
    int? EstimatedGrade = null,
    bool IsAssessment = false);

public record PerformanceBandDto(string Area, decimal Percent, string Rating);

public record MistakeDto(
    Guid AnswerId,
    Guid QuestionId,
    string QuestionType,
    string QuestionText,
    string? AnswerText,
    JsonElement? AnswerJson,
    decimal Score,
    decimal MaxScore,
    string? Feedback,
    IReadOnlyList<string> MissingCriteria,
    string? Explanation,
    string? CorrectAnswer,
    DateTime AnsweredAt,
    Guid SessionId,
    string SubjectName,
    string? TopicName,
    bool IsReviewed);
