namespace Tutor365.Application.DTOs;

public record AdminDashboardDto(
    int TotalStudents, int TotalParents, int TotalAdmins, int ActiveUsersLast7Days, int SessionsToday, int SessionsLast7Days,
    int QuestionsAnsweredToday, int QuestionsAnsweredLast7Days, decimal AveragePerformance, int LessonsCompletedLast7Days,
    int PublishedLessons, int PublishedQuestions, int PendingReviews, int AiRequestsLast7Days, int ErrorsLast24Hours,
    IReadOnlyList<CountByDto> StudentsByYear, IReadOnlyList<CountByDto> SubjectPerformance, IReadOnlyList<CountByDto> ActivityByDay);

public record CountByDto(string Label, decimal Value);

public record AdminUserDto(Guid Id, string Email, string FirstName, string LastName, string Role, bool EmailConfirmed, bool IsActive, bool MustChangePassword,
    DateTime? LastLoginAt, DateTime CreatedAt, Guid? ProfileId, string? YearGroup, int? ChildCount, IReadOnlyList<string>? LinkedNames);

public record AdminUserQuery(string? Search, string? Role, bool? IsActive, int Page = 1, int PageSize = 20);
public record CreateAdminUserRequest(string Email, string Password, string FirstName, string LastName, string Role = "Admin");
public record AdminUpdateUserRequest(string? FirstName, string? LastName, bool? IsActive, bool? EmailConfirmed);
public record AdminSetPasswordRequest(string NewPassword, bool MustChangePassword = true);

public record AuditLogDto(long Id, Guid? UserId, string? UserEmail, string? UserRole, string Action, string? EntityType, string? EntityId, string? Details, string? IpAddress, bool Success, DateTime CreatedAt);
public record AuditLogQuery(string? Action, Guid? UserId, string? Search, DateTime? From, DateTime? To, bool? Success, int Page = 1, int PageSize = 50);

public record SystemSettingDto(string Key, string Value, string? Description, bool IsPublic, DateTime UpdatedAt);
public record UpdateSystemSettingRequest(string Value, string? Description);

// Curriculum CRUD
public record UpsertTopicRequest(Guid QualificationId, string Code, string Name, string? Description, string? SpecificationReference, int SortOrder, int ExamWeight, string? Tier, int? YearNumber, string? Status);
public record UpsertSubTopicRequest(Guid TopicId, string Code, string Name, string? Description, string? SpecificationReference, int SortOrder, string? Tier, string? Status,
    string? ReferenceBookTitle, string? ReferenceBookIsbn, string? ReferenceBookSection, string? MappingNotes);
public record UpsertLessonRequest(Guid SubTopicId, string Title, string? Summary, IReadOnlyList<string>? Objectives, int EstimatedMinutes, int Difficulty, int SortOrder, string? Tier, string? Status);
public record UpsertLessonActivityRequest(int SortOrder, string Type, string Title, string? ContentMarkdown, Guid? QuestionId, int EstimatedMinutes, bool IsCheckpoint);
public record UpsertQuestionRequest(
    Guid SubTopicId, Guid? LessonId, string QuestionType, int Difficulty, string QuestionText, int MaxMarks, bool IsExamStyle, string? Tier,
    string? Hint, string? Explanation, string? ImageUrl, int? TimeLimitSeconds, string? Tags, string? Status, System.Text.Json.JsonElement? Metadata,
    IReadOnlyList<QuestionOptionInput>? Options, IReadOnlyList<QuestionAnswerInput>? Answers, IReadOnlyList<MarkSchemeInput>? MarkScheme);
public record QuestionOptionInput(string Text, bool IsCorrect, int SortOrder, string? MatchKey, string? Feedback);
public record QuestionAnswerInput(string AnswerText, bool IsCaseSensitive, decimal? NumericValue, decimal? NumericTolerance, string? Unit, int? BlankIndex, int Marks);
public record MarkSchemeInput(int SortOrder, string CriterionText, int Marks, IReadOnlyList<IReadOnlyList<string>>? Keywords);

public record AdminQuestionDto(Guid Id, Guid SubTopicId, string SubTopicName, string TopicName, string SubjectName, Guid? LessonId, string? LessonTitle,
    string QuestionType, int Difficulty, string QuestionText, int MaxMarks, bool IsExamStyle, string Tier, string? Hint, string? Explanation, string? ImageUrl,
    int? TimeLimitSeconds, string? Tags, string Status, System.Text.Json.JsonElement? Metadata,
    IReadOnlyList<QuestionOptionInput> Options, IReadOnlyList<QuestionAnswerInput> Answers, IReadOnlyList<MarkSchemeInput> MarkScheme, int TimesAnswered, decimal? CorrectRate);
public record AdminQuestionQuery(Guid? SubjectId, Guid? TopicId, Guid? SubTopicId, Guid? LessonId, string? Type, int? Difficulty, string? Search, string? Status, int Page = 1, int PageSize = 20);

public record ContentImportResultDto(int Files, int Lessons, int Questions, IReadOnlyList<string> Errors);
