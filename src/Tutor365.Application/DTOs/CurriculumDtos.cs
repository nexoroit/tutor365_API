namespace Tutor365.Application.DTOs;

public record ExamBoardDto(Guid Id, string Code, string Name, string? Website, bool IsActive);
public record YearGroupDto(Guid Id, int Number, string Name, string KeyStage);
public record SubjectDto(Guid Id, string Code, string Name, string? Description, string? ColourHex, string? Icon, int SortOrder, bool IsActive);
public record QualificationDto(Guid Id, Guid ExamBoardId, string ExamBoard, Guid SubjectId, string Subject, string Code, string Name, bool HasTiers, string? SpecificationUrl, int TopicCount);

public record TopicDto(
    Guid Id, Guid QualificationId, Guid SubjectId, string SubjectCode, string Code, string Name, string? Description,
    string? SpecificationReference, int SortOrder, int ExamWeight, string Tier, int? YearNumber, int SubTopicCount, int LessonCount, string Status);

public record SubTopicDto(
    Guid Id, Guid TopicId, string TopicName, string Code, string Name, string? Description, string? SpecificationReference,
    int SortOrder, string Tier, int LessonCount, int QuestionCount, CurriculumMappingDto? Mapping, string Status);

public record CurriculumMappingDto(Guid ExamBoardId, string ExamBoard, string? SpecificationReference, string? ReferenceBookTitle, string? ReferenceBookIsbn, string? ReferenceBookSection, string? Notes);

public record LessonSummaryDto(
    Guid Id, Guid SubTopicId, string SubTopicName, Guid TopicId, string TopicName, Guid SubjectId, string SubjectName,
    string Title, string? Summary, IReadOnlyList<string> Objectives, int EstimatedMinutes, int Difficulty, int SortOrder, string Tier, string Status, int ActivityCount, int QuestionCount);

public record LessonActivityDto(Guid Id, int SortOrder, string Type, string Title, string? ContentMarkdown, Guid? QuestionId, int EstimatedMinutes, bool IsCheckpoint);

public record LessonDetailDto(LessonSummaryDto Lesson, IReadOnlyList<LessonActivityDto> Activities);

public record CurriculumTreeDto(SubjectDto Subject, QualificationDto Qualification, IReadOnlyList<TopicTreeDto> Topics);
public record TopicTreeDto(TopicDto Topic, IReadOnlyList<SubTopicDto> SubTopics);
