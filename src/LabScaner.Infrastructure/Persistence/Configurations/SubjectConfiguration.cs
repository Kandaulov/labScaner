using LabScaner.Core.Subjects;
using LabScaner.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabScaner.Infrastructure.Persistence.Configurations;

internal sealed class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Code).HasMaxLength(32).IsRequired();
        builder.HasIndex(s => new { s.TeacherId, s.Code }).IsUnique();

        // Другие написания — массив text[] в PostgreSQL.
        builder.Ignore(s => s.Aliases);
        builder.Ignore(s => s.Tokens);
        builder.Property<List<string>>("_aliases").HasColumnName("aliases").IsRequired();

        builder.Property(s => s.FinalAssessment).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.AiReferenceText).HasMaxLength(20_000);
        builder.Property(s => s.DiskPathTemplate).HasMaxLength(400).IsRequired();

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(s => s.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
