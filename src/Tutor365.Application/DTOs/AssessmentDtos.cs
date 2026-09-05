namespace Tutor365.Application.DTOs;

public record AssessmentResultDto(
    Guid Id, Guid SessionId, string Type, string Title, Guid SubjectId, string SubjectName, string? SubjectColour, Guid? TopicId, string? TopicName,
    decimal Score, decimal MaxScore, decimal Percentage, int? EstimatedGrade, bool Passed, int PassMarkPercent, int? TimeLimitMinutes, int ElapsedSeconds, DateTime StartedAt, DateTime? CompletedAt);

public record ReadyTestDto(Guid TopicId, string TopicName, Guid SubjectId, string SubjectName, string? SubjectColour, int LessonsPassed, decimal? LastAssessmentPercent, int Attempts, bool Passed);

public record MockReadinessDto(Guid SubjectId, string SubjectName, string? SubjectColour, int TopicsStudied, int TopicsRequired, bool Available, decimal? LastMockPercent, int? LastMockGrade);

public record AssessmentsOverviewDto(IReadOnlyList<ReadyTestDto> TopicTests, IReadOnlyList<MockReadinessDto> Mocks, IReadOnlyList<AssessmentResultDto> Results);
