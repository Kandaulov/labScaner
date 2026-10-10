using LabScaner.Core.Connections;
using LabScaner.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LabScaner.Infrastructure.Persistence.Configurations;

internal sealed class MailConnectionConfiguration : IEntityTypeConfiguration<MailConnection>
{
    public void Configure(EntityTypeBuilder<MailConnection> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.TeacherId).IsUnique();
        builder.Property(c => c.Address).HasMaxLength(254).IsRequired();
        builder.Property(c => c.Login).HasMaxLength(254).IsRequired();
        builder.Property(c => c.PasswordProtected).HasMaxLength(4000).IsRequired();
        builder.Property(c => c.ImapHost).HasMaxLength(253).IsRequired();
        builder.Property(c => c.SmtpHost).HasMaxLength(253).IsRequired();
        builder.Property(c => c.ImapSecurity).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.SmtpSecurity).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.LastCheckMessage).HasMaxLength(500);
        builder.Ignore(c => c.Imap);
        builder.Ignore(c => c.Smtp);
        builder.Ignore(c => c.HasPassword);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.TeacherId).OnDelete(DeleteBehavior.Cascade);
    }
}
