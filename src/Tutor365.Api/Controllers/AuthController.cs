using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;

namespace Tutor365.Api.Controllers;

/// <summary>Registration, OTP verification, login, token refresh and password management.</summary>
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;
    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>Register a new parent account. Sends a 6-digit OTP to the email address.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterParentRequest request, CancellationToken ct)
        => Ok(await _auth.RegisterParentAsync(request, ct));

    /// <summary>Verify the registration OTP. On success the account is activated and tokens are returned.</summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken ct)
        => Ok(await _auth.VerifyEmailAsync(request, ct));

    /// <summary>Resend an OTP (purpose: Registration = 1, PasswordReset = 2).</summary>
    [HttpPost("resend-otp")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendOtp([FromBody] ResendOtpRequest request, CancellationToken ct)
        => Ok(await _auth.ResendOtpAsync(request, ct));

    /// <summary>Login for all roles. The backend determines the role from the account.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
        => Ok(await _auth.LoginAsync(request, ct));

    /// <summary>Exchange a refresh token for a new access/refresh token pair (rotating).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken ct)
        => Ok(await _auth.RefreshAsync(request, ct));

    /// <summary>Revoke the given refresh token (or all of the caller's tokens when none supplied).</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request, CancellationToken ct)
    {
        await _auth.LogoutAsync(request ?? new LogoutRequest(null), ct);
        return OkMessage("Logged out.");
    }

    /// <summary>Send a password-reset OTP. Always returns success to avoid account enumeration.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RegisterResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
        => Ok(await _auth.ForgotPasswordAsync(request, ct));

    /// <summary>Reset the password using the OTP sent by forgot-password.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        await _auth.ResetPasswordAsync(request, ct);
        return OkMessage("Your password has been reset. You can now log in.");
    }

    /// <summary>Change password for the logged-in user.</summary>
    [HttpPost("change-password")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        await _auth.ChangePasswordAsync(request, ct);
        return OkMessage("Password changed.");
    }

    /// <summary>Current user with their role-specific profile.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<MeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct) => Ok(await _auth.GetMeAsync(ct));
}
