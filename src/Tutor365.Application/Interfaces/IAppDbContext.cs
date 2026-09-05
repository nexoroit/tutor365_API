using Microsoft.EntityFrameworkCore;
using Tutor365.Domain.Entities;

namespace Tutor365.Application.Interfaces;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<OtpCode> OtpCodes { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<SystemSetting> SystemSettings { get; }

    DbSet<Parent> Parents { get; }
    DbSet<Student> Students { get; }
    DbSet<StudentParent> StudentParents { get; }
    DbSet<StudentSubjectSetting> StudentSubjectSettings { get; }
    DbSet<StudySchedule> StudySchedules { get; }

    DbSet<ExamBoard> ExamBoards { get; }
    DbSet<YearGroup> YearGroups { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<Qualification> Qualifications { get; }
    DbSet<Topic> Topics { get; }
    DbSet<SubTopic> SubTopics { get; }
    DbSet<CurriculumMapping> CurriculumMappings { get; }
    DbSet<Lesson> Lessons { get; }
    DbSet<LessonActivity> LessonActivities { get; }
    DbSet<Question> Questions { get; }
    DbSet<QuestionOption> QuestionOptions { get; }
    DbSet<QuestionAnswer> QuestionAnswers { get; }
    DbSet<MarkScheme> MarkSchemes { get; }
    DbSet<Assessment> Assessments { get; }
    DbSet<AssessmentQuestion> AssessmentQuestions { get; }

    DbSet<StudySession> StudySessions { get; }
    DbSet<SessionActivity> SessionActivities { get; }
    DbSet<StudentAnswer> StudentAnswers { get; }
    DbSet<AssessmentResult> AssessmentResults { get; }
    DbSet<StudentTopicProgress> StudentTopicProgress { get; }
    DbSet<StudentSubjectProgress> StudentSubjectProgress { get; }
    DbSet<StudentLessonProgress> StudentLessonProgress { get; }
    DbSet<StudyPlan> StudyPlans { get; }
    DbSet<StudyPlanItem> StudyPlanItems { get; }
    DbSet<DailyStudySlot> DailyStudySlots { get; }
    DbSet<AIConversation> AIConversations { get; }
    DbSet<AIConversationMessage> AIConversationMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    /// <summary>Detach everything after a failed save so the context can be reused.</summary>
    void ClearTracking();
}
