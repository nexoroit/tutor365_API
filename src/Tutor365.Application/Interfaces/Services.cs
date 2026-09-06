using Tutor365.Application.DTOs;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;

namespace Tutor365.Application.Interfaces;

public interface IOtpService
{
    Task<int> GenerateAndSendAsync(string email, string recipientName, OtpPurpose purpose, Guid? userId, CancellationToken ct = default);
    Task<bool> ValidateAndConsumeAsync(string email, OtpPurpose purpose, string code, CancellationToken ct = default);
}

public interface IAuthService
{
    Task<RegisterResponse> RegisterParentAsync(RegisterParentRequest request, CancellationToken ct = default);
    Task<AuthResponse> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct = default);
    Task<RegisterResponse> ResendOtpAsync(ResendOtpRequest request, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task LogoutAsync(LogoutRequest request, CancellationToken ct = default);
    Task<RegisterResponse> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default);
    Task<MeResponse> GetMeAsync(CancellationToken ct = default);
    Task<MeResponse> UpdateMeAsync(UpdateMeRequest request, CancellationToken ct = default);
    /// <summary>Verifies the password and sends an OTP to the new address.</summary>
    Task<RegisterResponse> RequestEmailChangeAsync(ChangeEmailRequest request, CancellationToken ct = default);
    /// <summary>Consumes the OTP sent to the new address and switches the account's email.</summary>
    Task<MeResponse> ConfirmEmailChangeAsync(ConfirmEmailChangeRequest request, CancellationToken ct = default);
    /// <summary>Active sign-ins (refresh tokens) for the current user.</summary>
    Task<IReadOnlyList<SessionDto>> GetSessionsAsync(CancellationToken ct = default);
    /// <summary>Revokes every refresh token for the current user (sign out everywhere).</summary>
    Task LogoutAllAsync(CancellationToken ct = default);
}

/// <summary>Central object-level authorisation: who may see which student.</summary>
public interface IAccessService
{
    /// <summary>Returns the student if the current user may access it (self, linked parent, admin); otherwise throws.</summary>
    Task<Student> GetAccessibleStudentAsync(Guid studentId, bool includeProfile = false, CancellationToken ct = default);
    /// <summary>Student id for the current Student user.</summary>
    Task<Guid> GetCurrentStudentIdAsync(CancellationToken ct = default);
    Task<Guid> GetCurrentParentIdAsync(CancellationToken ct = default);
    Task<bool> CanAccessStudentAsync(Guid studentId, CancellationToken ct = default);
}
