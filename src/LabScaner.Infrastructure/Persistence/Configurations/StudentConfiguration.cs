using LabScaner.Core.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabScaner.Infrastructure.Persistence.Configurations;

internal sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.LastName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.MiddleName).HasMaxLength(100);
        builder.Ignore(s => s.Name);
        builder.HasIndex(s => new { s.GroupId, s.LastName, s.FirstName });

        builder.HasMany(s => s.Emails)
            .WithOne()
            .HasForeignKey(e => e.StudentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Emails).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
