using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tutor365.Domain.Entities;

namespace Tutor365.Infrastructure.Data.Configurations;

public class ParentConfig : IEntityTypeConfiguration<Parent>
{
    public void Configure(EntityTypeBuilder<Parent> b)
    {
        b.ToTable("Parents");
        b.HasIndex(x => x.UserId).IsUnique();
        b.Property(x => x.Phone).HasMaxLength(30);
        b.HasOne(x => x.User).WithOne(u => u.Parent).HasForeignKey<Parent>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class StudentConfig : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> b)
    {
        b.ToTable("Students");
        b.HasIndex(x => x.UserId).IsUnique();
        b.Property(x => x.SchoolName).HasMaxLength(200);
        b.HasOne(x => x.User).WithOne(u => u.Student).HasForeignKey<Student>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.YearGroup).WithMany().HasForeignKey(x => x.YearGroupId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ExamBoard).WithMany().HasForeignKey(x => x.ExamBoardId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Schedule).WithOne(s => s.Student).HasForeignKey<StudySchedule>(s => s.StudentId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class StudentParentConfig : IEntityTypeConfiguration<StudentParent>
{
    public void Configure(EntityTypeBuilder<StudentParent> b)
    {
        b.ToTable("StudentParents");
        b.HasKey(x => new { x.StudentId, x.ParentId });
        b.Property(x => x.Relationship).HasMaxLength(50);
        b.HasOne(x => x.Student).WithMany(s => s.Parents).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Parent).WithMany(p => p.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StudentSubjectSettingConfig : IEntityTypeConfiguration<StudentSubjectSetting>
{
    public void Configure(EntityTypeBuilder<StudentSubjectSetting> b)
    {
        b.ToTable("StudentSubjectSettings");
        b.HasIndex(x => new { x.StudentId, x.SubjectId }).IsUnique();
        b.HasOne(x => x.Student).WithMany(s => s.SubjectSettings).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ExamBoard).WithMany().HasForeignKey(x => x.ExamBoardId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class StudyScheduleConfig : IEntityTypeConfiguration<StudySchedule>
{
    public void Configure(EntityTypeBuilder<StudySchedule> b)
    {
        b.ToTable("StudySchedules");
        b.HasIndex(x => x.StudentId).IsUnique();
    }
}
