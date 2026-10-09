using LabScaner.Core.Abstractions;
using LabScaner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Infrastructure.Identity;

/// <summary>Строка списка пользователей для администратора.</summary>
public sealed record UserRow(
    int Id,
    string Login,
    string DisplayName,
    bool IsAdmin,
    bool IsBlocked,
    DateTimeOffset? LockedOutUntil,
    bool MustChangePassword,
    IReadOnlyList<string> Subjects);

/// <summary>Результат операции: либо успех (с временным паролем, если он выдан), либо понятная ошибка.</summary>
public sealed record AdminResult(bool Succeeded, string? Error = null, string? TemporaryPassword = null, string? Login = null)
{
    public static AdminResult Fail(string error) => new(false, error);
}

/// <summary>
/// Управление пользователями (ADR-016): добавить преподавателя, сбросить пароль, заблокировать.
/// Правила: нельзя заблокировать себя и последнего действующего администратора.
/// </summary>
public sealed class UserAdministration(UserManager<AppUser> users, LabScanerDbContext db, IClock clock)
{
    public async Task<IReadOnlyList<UserRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var admins = (await users.GetUsersInRoleAsync(Roles.Admin)).Select(u => u.Id).ToHashSet();
        var subjects = await db.Subjects.IgnoreQueryFilters()
            .Where(s => s.IsActive)
            .Select(s => new { s.TeacherId, s.Code })
            .ToListAsync(cancellationToken);
        var all = await users.Users.OrderBy(u => u.DisplayName).ToListAsync(cancellationToken);
        var now = clock.UtcNow;

        return [.. all.Select(u => new UserRow(
            u.Id,
            u.UserName ?? string.Empty,
            u.DisplayName,
            admins.Contains(u.Id),
            u.IsBlocked,
            !u.IsBlocked && u.LockoutEnd > now ? u.LockoutEnd : null,
            u.MustChangePassword,
            [.. subjects.Where(s => s.TeacherId == u.Id).Select(s => s.Code).Order(StringComparer.Ordinal)]))];
    }

    public async Task<AdminResult> CreateTeacherAsync(string login, string displayName, bool isAdmin)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(displayName))
        {
            return AdminResult.Fail("Укажите логин и имя.");
        }

        var password = PasswordGenerator.Generate();
        var user = new AppUser
        {
            UserName = login.Trim(),
            DisplayName = displayName.Trim(),
            MustChangePassword = true,
        };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            return AdminResult.Fail(Describe(created));
        }

        await users.AddToRoleAsync(user, Roles.Teacher);
        if (isAdmin)
        {
            await users.AddToRoleAsync(user, Roles.Admin);
        }

        return new AdminResult(true, TemporaryPassword: password, Login: user.UserName);
    }

    /// <summary>Новый временный пароль; заодно снимает временную блокировку после неудачных попыток.</summary>
    public async Task<AdminResult> ResetPasswordAsync(int userId)
    {
        var user = await users.FindByIdAsync(userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (user is null)
        {
            return AdminResult.Fail("Пользователь не найден.");
        }

        var password = PasswordGenerator.Generate();
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, password);
        if (!reset.Succeeded)
        {
            return AdminResult.Fail(Describe(reset));
        }

        user.MustChangePassword = true;
        if (!user.IsBlocked)
        {
            user.LockoutEnd = null;
        }

        await users.ResetAccessFailedCountAsync(user);
        await users.UpdateAsync(user);
        return new AdminResult(true, TemporaryPassword: password, Login: user.UserName);
    }

    public async Task<AdminResult> SetBlockedAsync(int userId, bool blocked, int actingUserId)
    {
        if (blocked && userId == actingUserId)
        {
            return AdminResult.Fail("Нельзя заблокировать самого себя.");
        }

        var user = await users.FindByIdAsync(userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (user is null)
        {
            return AdminResult.Fail("Пользователь не найден.");
        }

        if (blocked && await users.IsInRoleAsync(user, Roles.Admin))
        {
            var activeAdmins = (await users.GetUsersInRoleAsync(Roles.Admin)).Count(a => !a.IsBlocked);
            if (activeAdmins <= 1)
            {
                return AdminResult.Fail("Это последний администратор — его нельзя заблокировать.");
            }
        }

        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, blocked ? DateTimeOffset.MaxValue : null);
        if (!blocked)
        {
            await users.ResetAccessFailedCountAsync(user);
        }

        // Смена штампа безопасности завершает уже открытые сеансы заблокированного пользователя.
        await users.UpdateSecurityStampAsync(user);
        return new AdminResult(true, Login: user.UserName);
    }

    /// <summary>Смена своего пароля; снимает требование сменить временный пароль.</summary>
    public async Task<AdminResult> ChangeOwnPasswordAsync(AppUser user, string currentPassword, string newPassword)
    {
        if (newPassword == currentPassword)
        {
            return AdminResult.Fail("Новый пароль должен отличаться от текущего.");
        }

        var changed = await users.ChangePasswordAsync(user, currentPassword, newPassword);
        if (!changed.Succeeded)
        {
            return AdminResult.Fail(Describe(changed));
        }

        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        return new AdminResult(true);
    }

    private static string Describe(IdentityResult result) => string.Join(" ", result.Errors.Select(e => e.Description));
}
