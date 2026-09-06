using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tutor365.Application.Common;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

public class AuthService : IAuthService
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly IOtpService _otp;
    private readonly ICurrentUser _current;
    private readonly IAuditService _audit;
    private readonly AuthOptions _options;
    private readonly ILogger<AuthService> _logger;
    private readonly IAppEmailService _emails;

    public AuthService(IAppDbContext db, IPasswordHasher hasher, ITokenService tokens, IOtpService otp,
        ICurrentUser current, IAuditService audit, IOptions<AuthOptions> options, ILogger<AuthService> logger, IAppEmailService emails)
    {
        _db = db; _hasher = hasher; _tokens = tokens; _otp = otp; _current = current; _audit = audit;
        _options = options.Value; _logger = logger; _emails = emails;
    }

    public async Task<RegisterResponse> RegisterParentAsync(RegisterParentRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim();
        var normalized = email.ToUpperInvariant();
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);

        if (existing != null)
        {
            if (existing.EmailConfirmed)
                throw new ConflictException("EMAIL_ALREADY_REGISTERED", "An account with this email already exists. Please log in or reset your password.");

            // Unverified account: refresh details and resend OTP.
            existing.FirstName = request.FirstName.Trim();
            existing.LastName = request.LastName.Trim();
            existing.PasswordHash = _hasher.Hash(request.Password);
            existing.UpdatedAt = DateTime.UtcNow;
            if (existing.Parent != null) existing.Parent.Phone = request.Phone;
            await _db.SaveChangesAsync(ct);
            var mins = await _otp.GenerateAndSendAsync(email, existing.FirstName, OtpPurpose.Registration, existing.Id, ct);
            return new RegisterResponse(email, true, mins, "A new verification code has been sent to your email.");
        }

        var user = new User
        {
            Email = email,
            NormalizedEmail = normalized,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PasswordHash = _hasher.Hash(request.Password),
            Role = UserRole.Parent,
            EmailConfirmed = false,
            Parent = new Parent { Phone = request.Phone?.Trim() }
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Auth.RegisterParent", "User", user.Id.ToString(), new { user.Email }, true, ct);

        var minutes = await _otp.GenerateAndSendAsync(email, user.FirstName, OtpPurpose.Registration, user.Id, ct);
        return new RegisterResponse(email, true, minutes, "Registration received. Enter the 6-digit code sent to your email to verify your account.");
    }

    public async Task<AuthResponse> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct = default)
    {
        var user = await FindUserByEmailAsync(request.Email, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "No account found for this email.", true);

        if (!user.EmailConfirmed)
        {
            var ok = await _otp.ValidateAndConsumeAsync(request.Email, OtpPurpose.Registration, request.Code, ct);
            if (!ok)
            {
                await _audit.LogAsync("Auth.VerifyEmail", "User", user.Id.ToString(), null, false, ct);
                throw new BusinessRuleException("INVALID_OTP", "The code is invalid or has expired. Please request a new one.");
            }
            user.EmailConfirmed = true;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync("Auth.VerifyEmail", "User", user.Id.ToString(), null, true, ct);
            if (user.Role == UserRole.Parent) await _emails.SendParentWelcomeAsync(user, ct);
        }

        return await IssueTokensAsync(user, rememberMe: false, ct);
    }

    public async Task<RegisterResponse> ResendOtpAsync(ResendOtpRequest request, CancellationToken ct = default)
    {
        var user = await FindUserByEmailAsync(request.Email, ct);
        // Do not reveal whether the account exists.
        if (user == null || !user.IsActive)
            return new RegisterResponse(request.Email, true, _options.OtpExpiryMinutes, "If the account exists, a code has been sent.");

        if (request.Purpose == OtpPurpose.Registration && user.EmailConfirmed)
            throw new BusinessRuleException("EMAIL_ALREADY_VERIFIED", "This email is already verified. Please log in.");

        var minutes = await _otp.GenerateAndSendAsync(user.Email, user.FirstName, request.Purpose, user.Id, ct);
        return new RegisterResponse(user.Email, true, minutes, "A new code has been sent to your email.");
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await FindUserByEmailAsync(request.Email, ct);
        if (user == null || !user.IsActive)
        {
            await _audit.LogAsync("Auth.Login", "User", null, new { request.Email, Reason = "UnknownOrInactive" }, false, ct);
            throw new UnauthorizedException("INVALID_CREDENTIALS", "Incorrect email or password.");
        }

        if (user.LockoutEndAt.HasValue && user.LockoutEndAt > DateTime.UtcNow)
            throw new UnauthorizedException("ACCOUNT_LOCKED", $"Too many failed attempts. Try again after {user.LockoutEndAt:HH:mm} UTC.");

        if (!_hasher.Verify(user.PasswordHash, request.Password))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= _options.LockoutThreshold)
            {
                user.LockoutEndAt = DateTime.UtcNow.AddMinutes(_options.LockoutMinutes);
                user.FailedLoginCount = 0;
            }
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync("Auth.Login", "User", user.Id.ToString(), new { Reason = "BadPassword" }, false, ct);
            throw new UnauthorizedException("INVALID_CREDENTIALS", "Incorrect email or password.");
        }

        if (!user.EmailConfirmed)
        {
            await _otp.GenerateAndSendAsync(user.Email, user.FirstName, OtpPurpose.Registration, user.Id, ct);
            throw new UnauthorizedException("EMAIL_NOT_VERIFIED", "Please verify your email. A new verification code has been sent.");
        }

        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
        user.LastLoginAt = DateTime.UtcNow;
        await _audit.LogAsync("Auth.Login", "User", user.Id.ToString(), null, true, ct);
        return await IssueTokensAsync(user, request.RememberMe, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken ct = default)
    {
        var hash = _tokens.HashToken(request.RefreshToken);
        var token = await _db.RefreshTokens.Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token == null)
            throw new UnauthorizedException("INVALID_REFRESH_TOKEN", "The refresh token is invalid.");

        if (!token.IsActive)
        {
            // Reuse of a revoked token: revoke the whole chain for safety.
            if (token.RevokedAt != null && token.ReplacedByTokenHash != null)
            {
                var descendants = await _db.RefreshTokens.Where(t => t.UserId == token.UserId && t.RevokedAt == null).ToListAsync(ct);
                foreach (var d in descendants) d.RevokedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                _logger.LogWarning("Refresh token reuse detected for user {UserId}", token.UserId);
            }
            throw new UnauthorizedException("INVALID_REFRESH_TOKEN", "The refresh token has expired or been revoked.");
        }

        if (!token.User.IsActive)
            throw new UnauthorizedException("ACCOUNT_DISABLED", "This account has been disabled.");

        var remaining = token.ExpiresAt - DateTime.UtcNow;
        var rememberMe = remaining.TotalDays > _options.RefreshTokenDays;
        var response = await IssueTokensAsync(token.User, rememberMe, ct, revoke: token);
        return response;
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var hash = _tokens.HashToken(request.RefreshToken);
            var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (token != null && token.RevokedAt == null)
            {
                token.RevokedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }
        else if (_current.UserId.HasValue)
        {
            var tokens = await _db.RefreshTokens.Where(t => t.UserId == _current.UserId && t.RevokedAt == null).ToListAsync(ct);
            foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        await _audit.LogAsync("Auth.Logout", "User", _current.UserId?.ToString(), null, true, ct);
    }

    public async Task<RegisterResponse> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var user = await FindUserByEmailAsync(request.Email, ct);
        if (user != null && user.IsActive)
            await _otp.GenerateAndSendAsync(user.Email, user.FirstName, OtpPurpose.PasswordReset, user.Id, ct);
        await _audit.LogAsync("Auth.ForgotPassword", "User", user?.Id.ToString(), new { request.Email }, user != null, ct);
        return new RegisterResponse(request.Email, true, _options.OtpExpiryMinutes, "If an account exists for this email, a reset code has been sent.");
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await FindUserByEmailAsync(request.Email, ct)
            ?? throw new BusinessRuleException("INVALID_OTP", "The code is invalid or has expired.");

        var ok = await _otp.ValidateAndConsumeAsync(request.Email, OtpPurpose.PasswordReset, request.Code, ct);
        if (!ok)
        {
            await _audit.LogAsync("Auth.ResetPassword", "User", user.Id.ToString(), null, false, ct);
            throw new BusinessRuleException("INVALID_OTP", "The code is invalid or has expired.");
        }

        user.PasswordHash = _hasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
        user.EmailConfirmed = true; // proves ownership of the mailbox
        user.UpdatedAt = DateTime.UtcNow;
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Auth.ResetPassword", "User", user.Id.ToString(), null, true, ct);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        var userId = _current.RequireUserId();
        var user = await _db.Users.FirstAsync(u => u.Id == userId, ct);
        if (!_hasher.Verify(user.PasswordHash, request.CurrentPassword))
            throw new BusinessRuleException("INVALID_CURRENT_PASSWORD", "The current password is incorrect.");
        user.PasswordHash = _hasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Auth.ChangePassword", "User", user.Id.ToString(), null, true, ct);
    }

    public async Task<MeResponse> GetMeAsync(CancellationToken ct = default)
    {
        var userId = _current.RequireUserId();
        var user = await _db.Users
            .Include(u => u.Student).ThenInclude(s => s!.YearGroup)
            .Include(u => u.Student).ThenInclude(s => s!.ExamBoard)
            .Include(u => u.Parent)
            .FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new UnauthorizedException();

        StudentProfileDto? student = null;
        ParentProfileDto? parent = null;

        if (user.Student != null)
        {
            var s = user.Student;
            student = new StudentProfileDto(s.Id, s.YearGroupId, s.YearGroup.Name, s.YearGroup.Number, s.ExamBoardId, s.ExamBoard.Name,
                s.TargetGrade, s.DateOfBirth, s.SchoolName, s.CurrentStreakDays, s.LongestStreakDays, s.TotalStudyMinutes);
        }
        if (user.Parent != null)
        {
            var count = await _db.StudentParents.CountAsync(sp => sp.ParentId == user.Parent.Id, ct);
            parent = new ParentProfileDto(user.Parent.Id, user.Parent.Phone, user.Parent.EmailNotifications, user.Parent.WeeklyReportEnabled, count);
        }

        return new MeResponse(ToSummary(user, ProfileIdOf(user)), student, parent);
    }

    // ---- helpers ----

    private async Task<User?> FindUserByEmailAsync(string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToUpperInvariant();
        return await _db.Users.Include(u => u.Parent).Include(u => u.Student)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, bool rememberMe, CancellationToken ct, RefreshToken? revoke = null)
    {
        if (user.Parent == null && user.Student == null)
        {
            await _db.Users.Entry(user).Reference(u => u.Parent).LoadAsync(ct);
            await _db.Users.Entry(user).Reference(u => u.Student).LoadAsync(ct);
        }
        var profileId = ProfileIdOf(user);
        var (access, accessExp) = _tokens.CreateAccessToken(user, profileId);
        var (raw, hash, _) = _tokens.CreateRefreshToken();
        var refreshExp = DateTime.UtcNow.AddDays(rememberMe ? _options.RememberMeRefreshTokenDays : _options.RefreshTokenDays);

        if (revoke != null)
        {
            revoke.RevokedAt = DateTime.UtcNow;
            revoke.ReplacedByTokenHash = hash;
        }

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            ExpiresAt = refreshExp,
            CreatedByIp = _current.IpAddress,
            UserAgent = _current.UserAgent?.Length > 500 ? _current.UserAgent[..500] : _current.UserAgent
        });
        await _db.SaveChangesAsync(ct);

        return new AuthResponse(access, accessExp, raw, refreshExp, ToSummary(user, profileId));
    }

    private static Guid? ProfileIdOf(User user) => user.Student?.Id ?? user.Parent?.Id;

    public static UserSummaryDto ToSummary(User user, Guid? profileId) =>
        new(user.Id, user.Email, user.FirstName, user.LastName, user.FullName, user.Role.ToString(), profileId,
            user.EmailConfirmed, user.MustChangePassword, user.AvatarUrl);
}
