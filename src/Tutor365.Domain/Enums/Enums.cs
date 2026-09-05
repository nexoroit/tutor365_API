namespace Tutor365.Domain.Enums;

public enum UserRole { Student = 1, Parent = 2, Admin = 4 }

public enum OtpPurpose { Registration = 1, PasswordReset = 2, EmailChange = 3 }

public enum KeyStage { KS3 = 3, KS4 = 4 }

public enum Tier { Foundation = 1, Higher = 2, NotApplicable = 3 }

public enum ContentStatus { Draft = 1, Published = 2, Archived = 3 }

public enum LessonActivityType
{
    Explanation = 1,
    Example = 2,
    Question = 3,
    Summary = 4,
    Checkpoint = 5,
    Reading = 6
}

public enum QuestionType
{
    MultipleChoice = 1,
    TrueFalse = 2,
    SingleAnswer = 3,
    MultipleAnswer = 4,
    FillInTheBlank = 5,
    ShortAnswer = 6,
    LongAnswer = 7,
    NumericalAnswer = 8,
    FormulaCalculation = 9,
    Equation = 10,
    Matching = 11,
    Ordering = 12,
    DragAndDrop = 13,
    DiagramLabelling = 14,
    ExamQuestion = 15
}

public enum StudySessionStatus { NotStarted = 0, Active = 1, Paused = 2, Completed = 3, Abandoned = 4 }

public enum StudySessionType { Lesson = 1, Assessment = 2, Review = 3, Practice = 4, Mock = 5 }

public enum SessionActivityStatus { Pending = 0, Current = 1, Completed = 2, Skipped = 3 }

public enum MarkingSource { Rule = 1, AI = 2, Tutor = 3, SelfAssessed = 4 }

public enum MasteryStatus { NotStarted = 0, NeedsPractice = 1, Developing = 2, Secure = 3, Mastered = 4 }

public enum LessonProgressStatus { Locked = 0, Available = 1, InProgress = 2, Completed = 3, Passed = 4 }

public enum StudyPlanStatus { Active = 1, Completed = 2, Cancelled = 3 }

public enum StudyPlanItemType { Lesson = 1, TopicReview = 2, TopicTest = 3, MockExam = 4 }

public enum StudyPlanItemStatus { Pending = 0, InProgress = 1, Completed = 2, Overdue = 3, Cancelled = 4 }

public enum StudyPlanPriority { Low = 1, Normal = 2, High = 3, Urgent = 4 }

public enum DailySlotStatus { Scheduled = 0, InProgress = 1, Completed = 2, Missed = 3, Skipped = 4 }

public enum AssessmentType { EndOfTopic = 1, EndOfSubTopic = 2, Mock = 3, TutorAssigned = 4, Diagnostic = 5 }

public enum AiMessageRole { System = 1, Tutor = 2, Student = 3 }

public enum NotificationType
{
    General = 0,
    StudyGoalCompleted = 1,
    SessionCompleted = 2,
    PerformanceAlert = 3,
    WeeklyReport = 4,
    WorkAssigned = 5,
    SystemAlert = 6,
    ChildActivity = 7
}

public enum StudentAttentionStatus { Good = 1, NeedsAttention = 2, InterventionRequired = 3, Inactive = 4 }

[Flags]
public enum DaysOfWeek
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    All = Weekdays | Saturday | Sunday
}
