using LabScaner.Core.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabScaner.Infrastructure.Persistence.Configurations;

internal sealed class TermConfiguration : IEntityTypeConfiguration<Term>
{
    public void Configure(EntityTypeBuilder<Term> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.AcademicYear).HasMaxLength(9).IsRequired();
        builder.Property(t => t.Season).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(t => new { t.AcademicYear, t.Season }).IsUnique();
    }
}
