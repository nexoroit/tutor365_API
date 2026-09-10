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
    /// <summary>Parent-controlled help in lessons: the hint button, "explain differently" and "show an example".</summary>
    public bool AllowHints { get; set; } = true;
    public bool AllowExplainDifferently { get; set; } = true;
    public bool AllowExamples { get; set; } = true;
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
    /// <summary>Per-weekday overrides as JSON {"Monday":{"sessions":2,"minutes":45},...}. Days not listed use SessionsPerDay/SessionMinutes.</summary>
    public string? DayPlansJson { get; set; }
    public TimeOnly? PreferredStartTime { get; set; }
    public bool AutoPlanEnabled { get; set; } = true;
    public Guid? UpdatedByUserId { get; set; }

    public static DaysOfWeek FlagFor(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => DaysOfWeek.Monday, DayOfWeek.Tuesday => DaysOfWeek.Tuesday, DayOfWeek.Wednesday => DaysOfWeek.Wednesday, DayOfWeek.Thursday => DaysOfWeek.Thursday,
        DayOfWeek.Friday => DaysOfWeek.Friday, DayOfWeek.Saturday => DaysOfWeek.Saturday, _ => DaysOfWeek.Sunday
    };

    public bool IsActive(DayOfWeek day) => ActiveDays.HasFlag(FlagFor(day));

    /// <summary>Sessions and minutes for a weekday: the per-day override when set, otherwise the schedule defaults. (0, 0) when the day is off.</summary>
    public (int Sessions, int Minutes) PlanFor(DayOfWeek day)
    {
        if (!IsActive(day)) return (0, 0);
        var plans = ReadDayPlans();
        return plans.TryGetValue(day, out var p) ? p : (SessionsPerDay, SessionMinutes);
    }

    public int WeeklyMinutes => Enum.GetValues<DayOfWeek>().Sum(d => { var (n, m) = PlanFor(d); return n * m; });
    public int WeeklySessions => Enum.GetValues<DayOfWeek>().Sum(d => PlanFor(d).Sessions);

    public Dictionary<DayOfWeek, (int Sessions, int Minutes)> ReadDayPlans()
    {
        var result = new Dictionary<DayOfWeek, (int, int)>();
        if (string.IsNullOrWhiteSpace(DayPlansJson)) return result;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(DayPlansJson);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!Enum.TryParse<DayOfWeek>(prop.Name, true, out var day)) continue;
                var sessions = prop.Value.TryGetProperty("sessions", out var se) && se.TryGetInt32(out var sv) ? sv : SessionsPerDay;
                var minutes = prop.Value.TryGetProperty("minutes", out var me) && me.TryGetInt32(out var mv) ? mv : SessionMinutes;
                result[day] = (sessions, minutes);
            }
        }
        catch { /* malformed JSON: fall back to defaults */ }
        return result;
    }

    public void WriteDayPlans(IReadOnlyDictionary<DayOfWeek, (int Sessions, int Minutes)> plans)
    {
        DayPlansJson = plans.Count == 0 ? null
            : System.Text.Json.JsonSerializer.Serialize(plans.ToDictionary(k => k.Key.ToString(), v => new { sessions = v.Value.Sessions, minutes = v.Value.Minutes }));
    }
}
