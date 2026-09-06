using Tutor365.Domain.Enums;

namespace Tutor365.Application.DTOs;

public record RegisterParentRequest(string Email, string Password, string FirstName, string LastName, string? Phone);
public record VerifyEmailRequest(string Email, string Code);
public record ResendOtpRequest(string Email, OtpPurpose Purpose);
public record LoginRequest(string Email, string Password, bool RememberMe = false);
public record RefreshTokenRequest(string RefreshToken);
public record LogoutRequest(string? RefreshToken);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Code, string NewPassword);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
/// <summary>Self-service profile update for any role. Null fields are left unchanged; SchoolName applies to students only.</summary>
public record UpdateMeRequest(string? FirstName, string? LastName, string? AvatarUrl, string? TimeZone, string? SchoolName);
public record ChangeEmailRequest(string NewEmail, string CurrentPassword);
public record ConfirmEmailChangeRequest(string NewEmail, string Code);
public record SessionDto(Guid Id, DateTime CreatedAt, DateTime ExpiresAt, string? IpAddress, string? UserAgent);

public record UserSummaryDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string FullName,
    string Role,
    Guid? ProfileId,
    bool EmailConfirmed,
    bool MustChangePassword,
    string? AvatarUrl,
    string? TimeZone);

public record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserSummaryDto User);

public record RegisterResponse(string Email, bool OtpSent, int OtpExpiryMinutes, string Message);

public record MeResponse(
    UserSummaryDto User,
    StudentProfileDto? Student,
    ParentProfileDto? Parent);

public record StudentProfileDto(
    Guid Id,
    Guid YearGroupId,
    string YearGroup,
    int YearNumber,
    Guid ExamBoardId,
    string ExamBoard,
    int TargetGrade,
    DateOnly? DateOfBirth,
    string? SchoolName,
    int CurrentStreakDays,
    int LongestStreakDays,
    int TotalStudyMinutes);

public record ParentProfileDto(Guid Id, string? Phone, bool EmailNotifications, bool WeeklyReportEnabled, int ChildCount);
