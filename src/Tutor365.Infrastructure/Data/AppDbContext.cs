using Microsoft.EntityFrameworkCore;
using Tutor365.Application.Interfaces;
using Tutor365.Domain.Common;
using Tutor365.Domain.Entities;

namespace Tutor365.Infrastructure.Data;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<StudentParent> StudentParents => Set<StudentParent>();
    public DbSet<StudentSubjectSetting> StudentSubjectSettings => Set<StudentSubjectSetting>();
    public DbSet<StudySchedule> StudySchedules => Set<StudySchedule>();

    public DbSet<ExamBoard> ExamBoards => Set<ExamBoard>();
    public DbSet<YearGroup> YearGroups => Set<YearGroup>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Qualification> Qualifications => Set<Qualification>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<SubTopic> SubTopics => Set<SubTopic>();
    public DbSet<CurriculumMapping> CurriculumMappings => Set<CurriculumMapping>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonActivity> LessonActivities => Set<LessonActivity>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();
    public DbSet<QuestionAnswer> QuestionAnswers => Set<QuestionAnswer>();
    public DbSet<MarkScheme> MarkSchemes => Set<MarkScheme>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<AssessmentQuestion> AssessmentQuestions => Set<AssessmentQuestion>();

    public DbSet<StudySession> StudySessions => Set<StudySession>();
    public DbSet<SessionActivity> SessionActivities => Set<SessionActivity>();
    public DbSet<StudentAnswer> StudentAnswers => Set<StudentAnswer>();
    public DbSet<AssessmentResult> AssessmentResults => Set<AssessmentResult>();
    public DbSet<StudentTopicProgress> StudentTopicProgress => Set<StudentTopicProgress>();
    public DbSet<StudentSubjectProgress> StudentSubjectProgress => Set<StudentSubjectProgress>();
    public DbSet<StudentLessonProgress> StudentLessonProgress => Set<StudentLessonProgress>();
    public DbSet<StudyPlan> StudyPlans => Set<StudyPlan>();
    public DbSet<StudyPlanItem> StudyPlanItems => Set<StudyPlanItem>();
    public DbSet<DailyStudySlot> DailyStudySlots => Set<DailyStudySlot>();
    public DbSet<AIConversation> AIConversations => Set<AIConversation>();
    public DbSet<AIConversationMessage> AIConversationMessages => Set<AIConversationMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Global conventions: decimals and enum storage.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                if (prop.ClrType == typeof(decimal) || prop.ClrType == typeof(decimal?))
                    prop.SetColumnType("decimal(18,4)");
            }
        }
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified) entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
