using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tutor365.Domain.Entities;

namespace Tutor365.Infrastructure.Data.Configurations;

public class StudySessionConfig : IEntityTypeConfiguration<StudySession>
{
    public void Configure(EntityTypeBuilder<StudySession> b)
    {
        b.ToTable("StudySessions");
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Lesson).WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Assessment).WithMany().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StudentId, x.Status });
        b.HasIndex(x => new { x.StudentId, x.CompletedAt });
        b.HasIndex(x => new { x.StudentId, x.LessonId });
    }
}

public class SessionActivityConfig : IEntityTypeConfiguration<SessionActivity>
{
    public void Configure(EntityTypeBuilder<SessionActivity> b)
    {
        b.ToTable("SessionActivities");
        b.HasOne(x => x.Session).WithMany(s => s.Activities).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.LessonActivity).WithMany().HasForeignKey(x => x.LessonActivityId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SessionId, x.SortOrder });
    }
}

public class StudentAnswerConfig : IEntityTypeConfiguration<StudentAnswer>
{
    public void Configure(EntityTypeBuilder<StudentAnswer> b)
    {
        b.ToTable("StudentAnswers");
        b.Property(x => x.Feedback).HasMaxLength(4000);
        b.HasOne(x => x.Session).WithMany(s => s.Answers).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StudentId, x.QuestionId });
        b.HasIndex(x => new { x.StudentId, x.IsCorrect, x.AnsweredAt });
    }
}

public class AssessmentResultConfig : IEntityTypeConfiguration<AssessmentResult>
{
    public void Configure(EntityTypeBuilder<AssessmentResult> b)
    {
        b.ToTable("AssessmentResults");
        b.HasOne(x => x.Assessment).WithMany().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StudentId, x.AssessmentId });
    }
}

public class StudentTopicProgressConfig : IEntityTypeConfiguration<StudentTopicProgress>
{
    public void Configure(EntityTypeBuilder<StudentTopicProgress> b)
    {
        b.ToTable("StudentTopicProgress");
        b.HasIndex(x => new { x.StudentId, x.TopicId }).IsUnique();
        b.HasIndex(x => new { x.StudentId, x.NextReviewAt });
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Topic).WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StudentSubjectProgressConfig : IEntityTypeConfiguration<StudentSubjectProgress>
{
    public void Configure(EntityTypeBuilder<StudentSubjectProgress> b)
    {
        b.ToTable("StudentSubjectProgress");
        b.HasIndex(x => new { x.StudentId, x.SubjectId }).IsUnique();
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StudentLessonProgressConfig : IEntityTypeConfiguration<StudentLessonProgress>
{
    public void Configure(EntityTypeBuilder<StudentLessonProgress> b)
    {
        b.ToTable("StudentLessonProgress");
        b.HasIndex(x => new { x.StudentId, x.LessonId }).IsUnique();
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Lesson).WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StudyPlanConfig : IEntityTypeConfiguration<StudyPlan>
{
    public void Configure(EntityTypeBuilder<StudyPlan> b)
    {
        b.ToTable("StudyPlans");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.StudentId, x.Status });
    }
}

public class StudyPlanItemConfig : IEntityTypeConfiguration<StudyPlanItem>
{
    public void Configure(EntityTypeBuilder<StudyPlanItem> b)
    {
        b.ToTable("StudyPlanItems");
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasOne(x => x.StudyPlan).WithMany(p => p.Items).HasForeignKey(x => x.StudyPlanId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Lesson).WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Assessment).WithMany().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class DailyStudySlotConfig : IEntityTypeConfiguration<DailyStudySlot>
{
    public void Configure(EntityTypeBuilder<DailyStudySlot> b)
    {
        b.ToTable("DailyStudySlots");
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasIndex(x => new { x.StudentId, x.Date, x.SlotNumber }).IsUnique();
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Lesson).WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AIConversationConfig : IEntityTypeConfiguration<AIConversation>
{
    public void Configure(EntityTypeBuilder<AIConversation> b)
    {
        b.ToTable("AIConversations");
        b.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.StudentId, x.SessionId });
    }
}

public class AIConversationMessageConfig : IEntityTypeConfiguration<AIConversationMessage>
{
    public void Configure(EntityTypeBuilder<AIConversationMessage> b)
    {
        b.ToTable("AIConversationMessages");
        b.Property(x => x.Message).IsRequired();
        b.Property(x => x.Intent).HasMaxLength(50);
        b.HasOne(x => x.Conversation).WithMany(c => c.Messages).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}
