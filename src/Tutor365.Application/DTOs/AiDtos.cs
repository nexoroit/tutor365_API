namespace Tutor365.Application.DTOs;

/// <summary>Intent: "message" | "hint" | "explain" | "example" | "why-wrong" | "easier" | "harder"</summary>
public record AiTutorRequest(Guid? SessionId, Guid? QuestionId, Guid? LessonId, string? Message, string? Intent, Guid? ConversationId);
public record AiTutorResponse(Guid ConversationId, string Reply, string Intent, bool IsStub, string ProviderName, IReadOnlyList<string> SuggestedActions);
public record AiConversationDto(Guid Id, Guid StudentId, Guid? SessionId, Guid? LessonId, DateTime CreatedAt, IReadOnlyList<AiMessageDto> Messages);
public record AiMessageDto(Guid Id, string Role, string Message, string? Intent, DateTime CreatedAt);
