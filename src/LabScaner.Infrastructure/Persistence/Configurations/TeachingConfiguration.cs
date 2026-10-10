using LabScaner.Core.Directory;
using LabScaner.Core.Subjects;
using LabScaner.Core.Teaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabScaner.Infrastructure.Persistence.Configurations;

internal sealed class SubjectTermConfiguration : IEntityTypeConfiguration<SubjectTerm>
{
    public void Configure(EntityTypeBuilder<SubjectTerm> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.DiskRootPath).HasMaxLength(500).IsRequired();
        builder.Property(s => s.GeneralRequirements).HasMaxLength(SubjectTerm.MaxGeneralRequirementsLength).IsRequired();
        builder.Ignore(s => s.Labs);
        builder.Ignore(s => s.Coursework);
        builder.Ignore(s => s.Title);
        builder.HasIndex(s => new { s.SubjectId, s.TermId }).IsUnique();

        builder.HasOne(s => s.Subject).WithMany().HasForeignKey(s => s.SubjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Term).WithMany().HasForeignKey(s => s.TermId).OnDelete(DeleteBehavior.Restrict);

        // Группы — общий справочник (ADR-016): связь через таблицу subject_term_groups.
        builder.HasMany(s => s.Groups).WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "SubjectTermGroups",
                r => r.HasOne<Group>().WithMany().HasForeignKey("GroupId").OnDelete(DeleteBehavior.Restrict),
                l => l.HasOne<SubjectTerm>().WithMany().HasForeignKey("SubjectTermId").OnDelete(DeleteBehavior.Cascade));
        builder.Navigation(s => s.Groups).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(s => s.Assignments).WithOne(a => a.SubjectTerm).HasForeignKey(a => a.SubjectTermId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Assignments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(s => s.Topics).WithOne(t => t.SubjectTerm).HasForeignKey(t => t.SubjectTermId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Topics).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.Title).HasMaxLength(Assignment.MaxTitleLength).IsRequired();
        builder.Property(a => a.TaskText).HasMaxLength(Assignment.MaxTaskTextLength).IsRequired();
        builder.Ignore(a => a.ShortName);
        builder.Ignore(a => a.HasTask);

        // Чек-лист — массив text[] в PostgreSQL.
        builder.Ignore(a => a.Checklist);
        builder.Property<List<string>>("_checklist").HasColumnName("checklist").IsRequired();
        builder.HasIndex(a => new { a.SubjectTermId, a.Kind, a.Number }).IsUnique();
    }
}

internal sealed class CourseworkTopicConfiguration : IEntityTypeConfiguration<CourseworkTopic>
{
    public void Configure(EntityTypeBuilder<CourseworkTopic> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Topic).HasMaxLength(300).IsRequired();
        builder.HasIndex(t => new { t.SubjectTermId, t.StudentId }).IsUnique();
        builder.HasOne<Student>().WithMany().HasForeignKey(t => t.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TaskDocumentConfiguration : IEntityTypeConfiguration<TaskDocument>
{
    public void Configure(EntityTypeBuilder<TaskDocument> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(d => d.FileName).HasMaxLength(255).IsRequired();
        builder.Property(d => d.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(d => d.Content).IsRequired();
        builder.HasIndex(d => new { d.SubjectTermId, d.Kind });
        builder.HasOne<SubjectTerm>().WithMany().HasForeignKey(d => d.SubjectTermId).OnDelete(DeleteBehavior.Cascade);
    }
}
