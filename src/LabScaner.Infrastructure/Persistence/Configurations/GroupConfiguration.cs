using LabScaner.Core.Directory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabScaner.Infrastructure.Persistence.Configurations;

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).HasMaxLength(32).IsRequired();
        builder.Property(g => g.NameKey).HasMaxLength(32).IsRequired();
        builder.Property(g => g.Direction).HasMaxLength(16).IsRequired();
        builder.HasIndex(g => g.NameKey).IsUnique();

        builder.HasMany(g => g.Students)
            .WithOne(s => s.Group)
            .HasForeignKey(s => s.GroupId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(g => g.Students).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
