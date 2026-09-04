using Tutor365.Domain.Common;
using Tutor365.Domain.Enums;

namespace Tutor365.Domain.Entities;

public class Parent : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public string? Phone { get; set; }
    public bool EmailNotifications { get; set; } = true;
    public bool WeeklyReportEnabled { get; set; } = true;
    public ICollection<StudentParent> Children { get; set; } = new List<StudentParent>();
}

public class Student : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;
    public Guid YearGroupId { get; set; }
    public YearGroup YearGroup { get; set; } = default!;
    public Guid ExamBoardId { get; set; }
    public ExamBoard ExamBoard { get; set; } = default!;
    public DateOnly? DateOfBirth { get; set; }
    public string? SchoolName { get; set; }
    /// <summary>Overall GCSE target grade (1-9).</summary>
    public int TargetGrade { get; set; } = 5;
    public int CurrentStreakDays { get; set; }
    public int LongestStreakDays { get; set; }
    public DateOnly? LastStudyDate { get; set; }
    public int TotalStudyMinutes { get; set; }
    public Guid? CreatedByParentId { get; set; }

    public ICollection<StudentParent> Parents { get; set; } = new List<StudentParent>();
    public ICollection<StudentSubjectSetting> SubjectSettings { get; set; } = new List<StudentSubjectSetting>();
    public StudySchedule? Schedule { get; set; }
}

public class StudentParent
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public Guid ParentId { get; set; }
    public Parent Parent { get; set; } = default!;
    public string Relationship { get; set; } = "Parent";
    public bool IsPrimary { get; set; }
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Per-student, per-subject configuration set by the parent (or tutor/admin).</summary>
public class StudentSubjectSetting : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public Guid SubjectId { get; set; }
    public Subject Subject { get; set; } = default!;
    public Guid? ExamBoardId { get; set; }
    public ExamBoard? ExamBoard { get; set; }
    public Tier Tier { get; set; } = Tier.Higher;
    public bool IsEnabled { get; set; } = true;
    /// <summary>Target GCSE grade 1-9 for this subject.</summary>
    public int TargetGrade { get; set; } = 5;
    /// <summary>Minimum percentage required on a lesson's questions before the next lesson unlocks.</summary>
    public int PassThresholdPercent { get; set; } = 70;
    /// <summary>Maximum re-attempts allowed before the student may move on anyway (0 = unlimited retries required).</summary>
    public int MaxAttemptsBeforeMoveOn { get; set; } = 3;
    public int Priority { get; set; } = 3;
}

public class StudySchedule : BaseEntity
{
    public Guid StudentId { get; set; }
    public Student Student { get; set; } = default!;
    public int SessionsPerDay { get; set; } = 2;
    public int SessionMinutes { get; set; } = 45;
    public DaysOfWeek ActiveDays { get; set; } = DaysOfWeek.All;
    public TimeOnly? PreferredStartTime { get; set; }
    public bool AutoPlanEnabled { get; set; } = true;
    public Guid? UpdatedByUserId { get; set; }
}
