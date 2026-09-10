namespace Tutor365.Application.DTOs;

public record CreateChildRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    Guid YearGroupId,
    Guid? ExamBoardId,
    DateOnly? DateOfBirth,
    string? SchoolName,
    int TargetGrade = 5,
    IReadOnlyList<Guid>? SubjectIds = null,
    int? SessionsPerDay = null,
    int? SessionMinutes = null);

public record UpdateChildRequest(
    string? FirstName,
    string? LastName,
    Guid? YearGroupId,
    Guid? ExamBoardId,
    DateOnly? DateOfBirth,
    string? SchoolName,
    int? TargetGrade);

public record SetChildPasswordRequest(string NewPassword);

public record UpdateParentProfileRequest(string? FirstName, string? LastName, string? Phone, bool? EmailNotifications, bool? WeeklyReportEnabled);

public record ChildSummaryDto(
    Guid StudentId,
    Guid UserId,
    string FirstName,
    string LastName,
    string FullName,
    string Email,
    Guid YearGroupId,
    string YearGroup,
    int YearNumber,
    Guid ExamBoardId,
    string ExamBoard,
    int TargetGrade,
    decimal OverallPercent,
    int TotalStudyMinutes,
    int StudyMinutesThisWeek,
    int SessionsThisWeek,
    int CurrentStreakDays,
    DateTime? LastActiveAt,
    bool IsActive,
    string? AvatarUrl);

public record ChildDetailDto(
    ChildSummaryDto Summary,
    DateOnly? DateOfBirth,
    string? SchoolName,
    StudyScheduleDto Schedule,
    IReadOnlyList<SubjectSettingDto> Subjects,
    HelpOptionsDto HelpOptions);

/// <summary>One weekday of the study schedule. Sessions/Minutes are 0 when the day is off.</summary>
public record DayScheduleDto(string Day, bool Active, int Sessions, int Minutes);

public record StudyScheduleDto(
    int SessionsPerDay,
    int SessionMinutes,
    IReadOnlyList<string> ActiveDays,
    TimeOnly? PreferredStartTime,
    bool AutoPlanEnabled,
    int WeeklyMinutes,
    IReadOnlyList<DayScheduleDto> Days,
    int WeeklySessions);

/// <summary>Set Days for a per-weekday plan (Monday..Sunday; sessions 1-6, minutes 20-120; Active=false switches the day off). The legacy fields still work as defaults for days without an entry.</summary>
public record UpdateStudyScheduleRequest(
    int? SessionsPerDay,
    int? SessionMinutes,
    IReadOnlyList<string>? ActiveDays,
    TimeOnly? PreferredStartTime,
    bool? AutoPlanEnabled,
    IReadOnlyList<DayScheduleInput>? Days = null);

public record DayScheduleInput(string Day, bool Active, int Sessions, int Minutes);

/// <summary>Which in-lesson help a student may use; set by the parent.</summary>
public record HelpOptionsDto(bool Hints, bool ExplainDifferently, bool Examples);
public record UpdateHelpOptionsRequest(bool? Hints, bool? ExplainDifferently, bool? Examples);

public record SubjectSettingDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    string? ColourHex,
    Guid? ExamBoardId,
    string? ExamBoard,
    string Tier,
    bool IsEnabled,
    int TargetGrade,
    int PassThresholdPercent,
    int MaxAttemptsBeforeMoveOn,
    int Priority);

public record UpdateSubjectSettingRequest(
    Guid SubjectId,
    Guid? ExamBoardId,
    string? Tier,
    bool? IsEnabled,
    int? TargetGrade,
    int? PassThresholdPercent,
    int? MaxAttemptsBeforeMoveOn,
    int? Priority);
