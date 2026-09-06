using Microsoft.EntityFrameworkCore;
using Tutor365.Application.DTOs;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Entities;
using Tutor365.Domain.Enums;
using Tutor365.Domain.Exceptions;

namespace Tutor365.Application.Services;

public interface IParentService
{
    Task<ParentProfileDto> UpdateProfileAsync(UpdateParentProfileRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ChildSummaryDto>> GetChildrenAsync(CancellationToken ct = default);
    Task<ChildDetailDto> GetChildAsync(Guid studentId, CancellationToken ct = default);
    Task<ChildDetailDto> CreateChildAsync(CreateChildRequest request, CancellationToken ct = default);
    Task<ChildDetailDto> UpdateChildAsync(Guid studentId, UpdateChildRequest request, CancellationToken ct = default);
    Task SetChildPasswordAsync(Guid studentId, SetChildPasswordRequest request, CancellationToken ct = default);
    Task SetChildActiveAsync(Guid studentId, bool isActive, CancellationToken ct = default);
    /// <summary>Permanently deletes the child's account and all learning history, freeing the email address.</summary>
    Task DeleteChildAsync(Guid studentId, CancellationToken ct = default);
    Task<StudyScheduleDto> GetScheduleAsync(Guid studentId, CancellationToken ct = default);
    Task<StudyScheduleDto> UpdateScheduleAsync(Guid studentId, UpdateStudyScheduleRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SubjectSettingDto>> GetSubjectSettingsAsync(Guid studentId, CancellationToken ct = default);
    Task<IReadOnlyList<SubjectSettingDto>> UpdateSubjectSettingsAsync(Guid studentId, IReadOnlyList<UpdateSubjectSettingRequest> requests, CancellationToken ct = default);
}

public class ParentService : IParentService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IAccessService _access;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;
    private readonly IAppEmailService _emails;

    public ParentService(IAppDbContext db, ICurrentUser current, IAccessService access, IPasswordHasher hasher, IAuditService audit, IAppEmailService emails)
    {
        _db = db; _current = current; _access = access; _hasher = hasher; _audit = audit; _emails = emails;
    }

    public async Task<ParentProfileDto> UpdateProfileAsync(UpdateParentProfileRequest request, CancellationToken ct = default)
    {
        var userId = _current.RequireUserId();
        var user = await _db.Users.Include(u => u.Parent).FirstAsync(u => u.Id == userId, ct);
        var parent = user.Parent ?? throw new ForbiddenException("Current user is not a parent.");
        if (!string.IsNullOrWhiteSpace(request.FirstName)) user.FirstName = request.FirstName.Trim();
        if (!string.IsNullOrWhiteSpace(request.LastName)) user.LastName = request.LastName.Trim();
        if (request.Phone != null) parent.Phone = request.Phone.Trim();
        if (request.EmailNotifications.HasValue) parent.EmailNotifications = request.EmailNotifications.Value;
        if (request.WeeklyReportEnabled.HasValue) parent.WeeklyReportEnabled = request.WeeklyReportEnabled.Value;
        user.UpdatedAt = parent.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        var count = await _db.StudentParents.CountAsync(sp => sp.ParentId == parent.Id, ct);
        return new ParentProfileDto(parent.Id, parent.Phone, parent.EmailNotifications, parent.WeeklyReportEnabled, count);
    }

    public async Task<IReadOnlyList<ChildSummaryDto>> GetChildrenAsync(CancellationToken ct = default)
    {
        var parentId = await _access.GetCurrentParentIdAsync(ct);
        var studentIds = await _db.StudentParents.Where(sp => sp.ParentId == parentId).Select(sp => sp.StudentId).ToListAsync(ct);
        var list = new List<ChildSummaryDto>();
        foreach (var id in studentIds) list.Add(await BuildSummaryAsync(id, ct));
        return list.OrderBy(c => c.FirstName).ToList();
    }

    public async Task<ChildDetailDto> GetChildAsync(Guid studentId, CancellationToken ct = default)
    {
        await _access.GetAccessibleStudentAsync(studentId, false, ct);
        return await BuildDetailAsync(studentId, ct);
    }

    public async Task<ChildDetailDto> CreateChildAsync(CreateChildRequest request, CancellationToken ct = default)
    {
        var parentId = await _access.GetCurrentParentIdAsync(ct);
        var parent = await _db.Parents.Include(p => p.User).FirstAsync(p => p.Id == parentId, ct);

        var normalized = request.Email.Trim().ToUpperInvariant();
        if (await _db.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct))
            throw new ConflictException("EMAIL_ALREADY_REGISTERED", "An account with this email already exists.");

        var year = await _db.YearGroups.FirstOrDefaultAsync(y => y.Id == request.YearGroupId, ct)
            ?? throw new NotFoundException("YearGroup", request.YearGroupId);
        var examBoardId = request.ExamBoardId ?? await _db.ExamBoards.Where(b => b.Code == "AQA").Select(b => b.Id).FirstAsync(ct);
        if (!await _db.ExamBoards.AnyAsync(b => b.Id == examBoardId && b.IsActive, ct))
            throw new NotFoundException("ExamBoard", examBoardId);

        var subjects = await _db.Subjects.Where(s => s.IsActive).OrderBy(s => s.SortOrder).ToListAsync(ct);
        if (request.SubjectIds is { Count: > 0 })
            subjects = subjects.Where(s => request.SubjectIds.Contains(s.Id)).ToList();

        var user = new User
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalized,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PasswordHash = _hasher.Hash(request.Password),
            Role = UserRole.Student,
            EmailConfirmed = true, // parent vouches for the child's email; child can reset password via OTP later
            Student = new Student
            {
                YearGroupId = year.Id,
                ExamBoardId = examBoardId,
                DateOfBirth = request.DateOfBirth,
                SchoolName = request.SchoolName?.Trim(),
                TargetGrade = request.TargetGrade,
                CreatedByParentId = parentId,
                Schedule = new StudySchedule
                {
                    SessionsPerDay = request.SessionsPerDay ?? 2,
                    SessionMinutes = request.SessionMinutes ?? 45,
                    UpdatedByUserId = parent.UserId
                }
            }
        };
        foreach (var s in subjects)
            user.Student.SubjectSettings.Add(new StudentSubjectSetting
            {
                SubjectId = s.Id, ExamBoardId = examBoardId, TargetGrade = request.TargetGrade,
                Tier = request.TargetGrade >= 5 ? Tier.Higher : Tier.Foundation
            });
        user.Student.Parents.Add(new StudentParent { ParentId = parentId, IsPrimary = true });

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Parent.CreateChild", "Student", user.Student.Id.ToString(), new { user.Email, Year = year.Number }, true, ct);
        await _emails.SendChildCreatedAsync(parent.User, user, ct);
        return await BuildDetailAsync(user.Student.Id, ct);
    }

    public async Task<ChildDetailDto> UpdateChildAsync(Guid studentId, UpdateChildRequest request, CancellationToken ct = default)
    {
        EnsureParentOrAdmin();
        var student = await _access.GetAccessibleStudentAsync(studentId, true, ct);
        if (!string.IsNullOrWhiteSpace(request.FirstName)) student.User.FirstName = request.FirstName.Trim();
        if (!string.IsNullOrWhiteSpace(request.LastName)) student.User.LastName = request.LastName.Trim();
        if (request.YearGroupId.HasValue)
        {
            if (!await _db.YearGroups.AnyAsync(y => y.Id == request.YearGroupId, ct)) throw new NotFoundException("YearGroup", request.YearGroupId);
            student.YearGroupId = request.YearGroupId.Value;
        }
        if (request.ExamBoardId.HasValue)
        {
            if (!await _db.ExamBoards.AnyAsync(b => b.Id == request.ExamBoardId, ct)) throw new NotFoundException("ExamBoard", request.ExamBoardId);
            student.ExamBoardId = request.ExamBoardId.Value;
        }
        if (request.DateOfBirth.HasValue) student.DateOfBirth = request.DateOfBirth;
        if (request.SchoolName != null) student.SchoolName = request.SchoolName.Trim();
        if (request.TargetGrade.HasValue) student.TargetGrade = request.TargetGrade.Value;
        student.UpdatedAt = student.User.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Parent.UpdateChild", "Student", studentId.ToString(), request, true, ct);
        return await BuildDetailAsync(studentId, ct);
    }

    public async Task SetChildPasswordAsync(Guid studentId, SetChildPasswordRequest request, CancellationToken ct = default)
    {
        EnsureParentOrAdmin();
        var student = await _access.GetAccessibleStudentAsync(studentId, true, ct);
        student.User.PasswordHash = _hasher.Hash(request.NewPassword);
        student.User.FailedLoginCount = 0;
        student.User.LockoutEndAt = null;
        student.User.UpdatedAt = DateTime.UtcNow;
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == student.UserId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Parent.SetChildPassword", "Student", studentId.ToString(), null, true, ct);
    }

    public async Task SetChildActiveAsync(Guid studentId, bool isActive, CancellationToken ct = default)
    {
        EnsureParentOrAdmin();
        var student = await _access.GetAccessibleStudentAsync(studentId, true, ct);
        student.User.IsActive = isActive;
        student.User.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Parent.SetChildActive", "Student", studentId.ToString(), new { isActive }, true, ct);
    }

    public async Task DeleteChildAsync(Guid studentId, CancellationToken ct = default)
    {
        EnsureParentOrAdmin();
        var student = await _access.GetAccessibleStudentAsync(studentId, true, ct);
        var user = student.User;
        // Rows without a cascading FK to the student.
        var results = await _db.AssessmentResults.Where(r => r.StudentId == studentId).ToListAsync(ct);
        _db.AssessmentResults.RemoveRange(results);
        var otps = await _db.OtpCodes.Where(o => o.UserId == user.Id).ToListAsync(ct);
        _db.OtpCodes.RemoveRange(otps);
        // Sessions cascade their activities and answers; progress, slots, plans, conversations and parent links cascade from the student; tokens and notifications from the user.
        _db.Students.Remove(student);
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("Parent.DeleteChild", "Student", studentId.ToString(), new { user.Email, user.FullName }, true, ct);
    }

    public async Task<StudyScheduleDto> GetScheduleAsync(Guid studentId, CancellationToken ct = default)
    {
        await _access.GetAccessibleStudentAsync(studentId, false, ct);
        var schedule = await GetOrCreateScheduleAsync(studentId, ct);
        return ToDto(schedule);
    }

    public async Task<StudyScheduleDto> UpdateScheduleAsync(Guid studentId, UpdateStudyScheduleRequest request, CancellationToken ct = default)
    {
        EnsureParentOrAdmin();
        await _access.GetAccessibleStudentAsync(studentId, false, ct);
        var schedule = await GetOrCreateScheduleAsync(studentId, ct);
        if (request.SessionsPerDay.HasValue) schedule.SessionsPerDay = request.SessionsPerDay.Value;
        if (request.SessionMinutes.HasValue) schedule.SessionMinutes = request.SessionMinutes.Value;
        if (request.ActiveDays != null) schedule.ActiveDays = ParseDays(request.ActiveDays);
        if (request.PreferredStartTime.HasValue) schedule.PreferredStartTime = request.PreferredStartTime;
        if (request.AutoPlanEnabled.HasValue) schedule.AutoPlanEnabled = request.AutoPlanEnabled.Value;
        schedule.UpdatedByUserId = _current.UserId;
        schedule.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await DropPlannedSlotsAsync(studentId, ct);
        await _audit.LogAsync("Parent.UpdateSchedule", "Student", studentId.ToString(), request, true, ct);
        return ToDto(schedule);
    }

    public async Task<IReadOnlyList<SubjectSettingDto>> GetSubjectSettingsAsync(Guid studentId, CancellationToken ct = default)
    {
        await _access.GetAccessibleStudentAsync(studentId, false, ct);
        return await QuerySettingsAsync(studentId, ct);
    }

    public async Task<IReadOnlyList<SubjectSettingDto>> UpdateSubjectSettingsAsync(Guid studentId, IReadOnlyList<UpdateSubjectSettingRequest> requests, CancellationToken ct = default)
    {
        EnsureParentOrAdmin();
        await _access.GetAccessibleStudentAsync(studentId, false, ct);
        var settings = await _db.StudentSubjectSettings.Where(s => s.StudentId == studentId).ToListAsync(ct);

        foreach (var r in requests)
        {
            var setting = settings.FirstOrDefault(s => s.SubjectId == r.SubjectId);
            if (setting == null)
            {
                if (!await _db.Subjects.AnyAsync(s => s.Id == r.SubjectId, ct)) throw new NotFoundException("Subject", r.SubjectId);
                setting = new StudentSubjectSetting { StudentId = studentId, SubjectId = r.SubjectId };
                _db.StudentSubjectSettings.Add(setting);
                settings.Add(setting);
            }
            if (r.ExamBoardId.HasValue) setting.ExamBoardId = r.ExamBoardId;
            if (r.Tier != null && Enum.TryParse<Tier>(r.Tier, true, out var tier)) setting.Tier = tier;
            if (r.IsEnabled.HasValue) setting.IsEnabled = r.IsEnabled.Value;
            if (r.TargetGrade.HasValue) setting.TargetGrade = r.TargetGrade.Value;
            if (r.PassThresholdPercent.HasValue) setting.PassThresholdPercent = r.PassThresholdPercent.Value;
            if (r.MaxAttemptsBeforeMoveOn.HasValue) setting.MaxAttemptsBeforeMoveOn = r.MaxAttemptsBeforeMoveOn.Value;
            if (r.Priority.HasValue) setting.Priority = r.Priority.Value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        await DropPlannedSlotsAsync(studentId, ct);
        await _audit.LogAsync("Parent.UpdateSubjectSettings", "Student", studentId.ToString(), requests, true, ct);
        return await QuerySettingsAsync(studentId, ct);
    }

    // ---- helpers ----

    /// <summary>Settings changed: discard future not-yet-started slots so the timetable is rebuilt with the new inputs.</summary>
    private async Task DropPlannedSlotsAsync(Guid studentId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var planned = await _db.DailyStudySlots.Where(s => s.StudentId == studentId && s.Date >= today && s.Status == DailySlotStatus.Scheduled).ToListAsync(ct);
        _db.DailyStudySlots.RemoveRange(planned);
        await _db.SaveChangesAsync(ct);
    }

    private void EnsureParentOrAdmin()
    {
        if (_current.Role is not (UserRole.Parent or UserRole.Admin)) throw new ForbiddenException();
    }

    private async Task<StudySchedule> GetOrCreateScheduleAsync(Guid studentId, CancellationToken ct)
    {
        var schedule = await _db.StudySchedules.FirstOrDefaultAsync(s => s.StudentId == studentId, ct);
        if (schedule == null)
        {
            schedule = new StudySchedule { StudentId = studentId };
            _db.StudySchedules.Add(schedule);
            await _db.SaveChangesAsync(ct);
        }
        return schedule;
    }

    public static StudyScheduleDto ToDto(StudySchedule s)
    {
        var days = Enum.GetValues<DaysOfWeek>()
            .Where(d => d is not (DaysOfWeek.None or DaysOfWeek.Weekdays or DaysOfWeek.All) && s.ActiveDays.HasFlag(d))
            .Select(d => d.ToString()).ToList();
        return new StudyScheduleDto(s.SessionsPerDay, s.SessionMinutes, days, s.PreferredStartTime, s.AutoPlanEnabled,
            s.SessionsPerDay * s.SessionMinutes * days.Count);
    }

    private static DaysOfWeek ParseDays(IReadOnlyList<string> days)
    {
        var result = DaysOfWeek.None;
        foreach (var d in days)
            if (Enum.TryParse<DaysOfWeek>(d, true, out var v)) result |= v;
        return result == DaysOfWeek.None ? DaysOfWeek.All : result;
    }

    private async Task<IReadOnlyList<SubjectSettingDto>> QuerySettingsAsync(Guid studentId, CancellationToken ct) =>
        await _db.StudentSubjectSettings.Where(s => s.StudentId == studentId)
            .OrderBy(s => s.Subject.SortOrder)
            .Select(s => new SubjectSettingDto(s.SubjectId, s.Subject.Code, s.Subject.Name, s.Subject.ColourHex,
                s.ExamBoardId, s.ExamBoard != null ? s.ExamBoard.Name : null, s.Tier.ToString(), s.IsEnabled,
                s.TargetGrade, s.PassThresholdPercent, s.MaxAttemptsBeforeMoveOn, s.Priority))
            .ToListAsync(ct);

    private async Task<ChildSummaryDto> BuildSummaryAsync(Guid studentId, CancellationToken ct)
    {
        var s = await _db.Students.Include(x => x.User).Include(x => x.YearGroup).Include(x => x.ExamBoard).FirstAsync(x => x.Id == studentId, ct);
        var weekStart = DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek + 1);
        if (weekStart > DateTime.UtcNow) weekStart = weekStart.AddDays(-7);

        var weekSessions = await _db.StudySessions
            .Where(x => x.StudentId == studentId && x.Status == StudySessionStatus.Completed && x.CompletedAt >= weekStart)
            .Select(x => x.ElapsedSeconds).ToListAsync(ct);
        var overall = await _db.StudentSubjectProgress.Where(p => p.StudentId == studentId && p.QuestionsAttempted > 0)
            .Select(p => (decimal?)p.AverageScore).AverageAsync(ct) ?? 0;
        var lastActive = await _db.StudySessions.Where(x => x.StudentId == studentId).MaxAsync(x => (DateTime?)x.LastActivityAt, ct);

        return new ChildSummaryDto(s.Id, s.UserId, s.User.FirstName, s.User.LastName, s.User.FullName, s.User.Email,
            s.YearGroupId, s.YearGroup.Name, s.YearGroup.Number, s.ExamBoardId, s.ExamBoard.Name, s.TargetGrade,
            Math.Round(overall, 1), s.TotalStudyMinutes, weekSessions.Sum() / 60, weekSessions.Count, s.CurrentStreakDays,
            lastActive ?? s.User.LastLoginAt, s.User.IsActive, s.User.AvatarUrl);
    }

    private async Task<ChildDetailDto> BuildDetailAsync(Guid studentId, CancellationToken ct)
    {
        var summary = await BuildSummaryAsync(studentId, ct);
        var s = await _db.Students.FirstAsync(x => x.Id == studentId, ct);
        var schedule = await GetOrCreateScheduleAsync(studentId, ct);
        var subjects = await QuerySettingsAsync(studentId, ct);
        return new ChildDetailDto(summary, s.DateOfBirth, s.SchoolName, ToDto(schedule), subjects);
    }
}
