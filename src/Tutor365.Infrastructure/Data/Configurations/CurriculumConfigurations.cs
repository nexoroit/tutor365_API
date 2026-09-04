using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tutor365.Domain.Entities;

namespace Tutor365.Infrastructure.Data.Configurations;

public class ExamBoardConfig : IEntityTypeConfiguration<ExamBoard>
{
    public void Configure(EntityTypeBuilder<ExamBoard> b)
    {
        b.ToTable("ExamBoards");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Website).HasMaxLength(300);
    }
}

public class YearGroupConfig : IEntityTypeConfiguration<YearGroup>
{
    public void Configure(EntityTypeBuilder<YearGroup> b)
    {
        b.ToTable("YearGroups");
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.Name).HasMaxLength(50).IsRequired();
    }
}

public class SubjectConfig : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> b)
    {
        b.ToTable("Subjects");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.ColourHex).HasMaxLength(9);
        b.Property(x => x.Icon).HasMaxLength(50);
    }
}

public class QualificationConfig : IEntityTypeConfiguration<Qualification>
{
    public void Configure(EntityTypeBuilder<Qualification> b)
    {
        b.ToTable("Qualifications");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.SpecificationUrl).HasMaxLength(500);
        b.HasIndex(x => new { x.ExamBoardId, x.Code }).IsUnique();
        b.HasOne(x => x.ExamBoard).WithMany(e => e.Qualifications).HasForeignKey(x => x.ExamBoardId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TopicConfig : IEntityTypeConfiguration<Topic>
{
    public void Configure(EntityTypeBuilder<Topic> b)
    {
        b.ToTable("Topics");
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.HasIndex(x => new { x.QualificationId, x.Code }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.SpecificationReference).HasMaxLength(50);
        b.HasOne(x => x.Qualification).WithMany(q => q.Topics).HasForeignKey(x => x.QualificationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.YearGroup).WithMany().HasForeignKey(x => x.YearGroupId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SubjectId, x.SortOrder });
    }
}

public class SubTopicConfig : IEntityTypeConfiguration<SubTopic>
{
    public void Configure(EntityTypeBuilder<SubTopic> b)
    {
        b.ToTable("SubTopics");
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.HasIndex(x => new { x.TopicId, x.Code }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.SpecificationReference).HasMaxLength(50);
        b.HasOne(x => x.Topic).WithMany(t => t.SubTopics).HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class CurriculumMappingConfig : IEntityTypeConfiguration<CurriculumMapping>
{
    public void Configure(EntityTypeBuilder<CurriculumMapping> b)
    {
        b.ToTable("CurriculumMappings");
        b.Property(x => x.SpecificationReference).HasMaxLength(50);
        b.Property(x => x.ReferenceBookTitle).HasMaxLength(200);
        b.Property(x => x.ReferenceBookIsbn).HasMaxLength(20);
        b.Property(x => x.ReferenceBookSection).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.SubTopicId, x.ExamBoardId }).IsUnique();
        b.HasOne(x => x.SubTopic).WithMany(s => s.Mappings).HasForeignKey(x => x.SubTopicId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ExamBoard).WithMany().HasForeignKey(x => x.ExamBoardId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class LessonConfig : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> b)
    {
        b.ToTable("Lessons");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(2000);
        b.HasOne(x => x.SubTopic).WithMany(s => s.Lessons).HasForeignKey(x => x.SubTopicId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.SubTopicId, x.SortOrder });
    }
}

public class LessonActivityConfig : IEntityTypeConfiguration<LessonActivity>
{
    public void Configure(EntityTypeBuilder<LessonActivity> b)
    {
        b.ToTable("LessonActivities");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.HasOne(x => x.Lesson).WithMany(l => l.Activities).HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.LessonId, x.SortOrder });
    }
}

public class QuestionConfig : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> b)
    {
        b.ToTable("Questions");
        b.Property(x => x.QuestionText).IsRequired();
        b.Property(x => x.ImageUrl).HasMaxLength(500);
        b.Property(x => x.Hint).HasMaxLength(2000);
        b.Property(x => x.Tags).HasMaxLength(300);
        b.HasOne(x => x.SubTopic).WithMany().HasForeignKey(x => x.SubTopicId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.SubTopicId, x.Difficulty });
        b.HasIndex(x => x.LessonId);
    }
}

public class QuestionOptionConfig : IEntityTypeConfiguration<QuestionOption>
{
    public void Configure(EntityTypeBuilder<QuestionOption> b)
    {
        b.ToTable("QuestionOptions");
        b.Property(x => x.Text).HasMaxLength(1000).IsRequired();
        b.Property(x => x.MatchKey).HasMaxLength(200);
        b.Property(x => x.Feedback).HasMaxLength(1000);
        b.HasOne(x => x.Question).WithMany(q => q.Options).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class QuestionAnswerConfig : IEntityTypeConfiguration<QuestionAnswer>
{
    public void Configure(EntityTypeBuilder<QuestionAnswer> b)
    {
        b.ToTable("QuestionAnswers");
        b.Property(x => x.AnswerText).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(30);
        b.HasOne(x => x.Question).WithMany(q => q.AcceptedAnswers).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class MarkSchemeConfig : IEntityTypeConfiguration<MarkScheme>
{
    public void Configure(EntityTypeBuilder<MarkScheme> b)
    {
        b.ToTable("MarkSchemes");
        b.Property(x => x.CriterionText).HasMaxLength(1000).IsRequired();
        b.HasOne(x => x.Question).WithMany(q => q.MarkScheme).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AssessmentConfig : IEntityTypeConfiguration<Assessment>
{
    public void Configure(EntityTypeBuilder<Assessment> b)
    {
        b.ToTable("Assessments");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Topic).WithMany().HasForeignKey(x => x.TopicId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AssessmentQuestionConfig : IEntityTypeConfiguration<AssessmentQuestion>
{
    public void Configure(EntityTypeBuilder<AssessmentQuestion> b)
    {
        b.ToTable("AssessmentQuestions");
        b.HasKey(x => new { x.AssessmentId, x.QuestionId });
        b.HasOne(x => x.Assessment).WithMany(a => a.Questions).HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
    }
}
