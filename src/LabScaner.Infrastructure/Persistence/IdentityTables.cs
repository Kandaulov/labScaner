using LabScaner.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Infrastructure.Persistence;

/// <summary>Таблицы Identity под общими правилами именования: <c>AspNetUsers</c> → <c>users</c>.</summary>
internal static class IdentityTables
{
    public static void Rename(ModelBuilder builder)
    {
        builder.Entity<AppUser>(user =>
        {
            user.ToTable("users");
            user.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
            user.Ignore(u => u.IsBlocked);
        });
        builder.Entity<IdentityRole<int>>().ToTable("roles");
        builder.Entity<IdentityUserRole<int>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<int>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<int>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<int>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<int>>().ToTable("role_claims");
    }
}
