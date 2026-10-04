using LabScaner.Core.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabScaner.Infrastructure.Persistence.Configurations;

internal sealed class StudentEmailConfiguration : IEntityTypeConfiguration<StudentEmail>
{
    public void Configure(EntityTypeBuilder<StudentEmail> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Email).HasMaxLength(254).IsRequired();
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(16);

        // Один адрес может прийти от двух студентов (ADR-024) — уникальность только в пределах студента.
        builder.HasIndex(e => new { e.StudentId, e.Email }).IsUnique();
        builder.HasIndex(e => e.Email);
    }
}
