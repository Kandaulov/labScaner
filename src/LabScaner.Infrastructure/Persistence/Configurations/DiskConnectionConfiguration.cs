using LabScaner.Core.Connections;
using LabScaner.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabScaner.Infrastructure.Persistence.Configurations;

internal sealed class DiskConnectionConfiguration : IEntityTypeConfiguration<DiskConnection>
{
    public void Configure(EntityTypeBuilder<DiskConnection> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.TeacherId).IsUnique();
        builder.Property(c => c.AccessTokenProtected).HasMaxLength(4000).IsRequired();
        builder.Property(c => c.RefreshTokenProtected).HasMaxLength(4000);
        builder.Property(c => c.Login).HasMaxLength(100).IsRequired();
        builder.Property(c => c.DisplayName).HasMaxLength(200);
        builder.Property(c => c.LastCheckMessage).HasMaxLength(500);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.TeacherId).OnDelete(DeleteBehavior.Cascade);
    }
}
