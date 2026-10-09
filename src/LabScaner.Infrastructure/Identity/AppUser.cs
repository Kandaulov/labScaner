using Microsoft.AspNetCore.Identity;

namespace LabScaner.Infrastructure.Identity;

/// <summary>Пользователь системы — преподаватель. Входит по логину (<see cref="IdentityUser{TKey}.UserName"/>).</summary>
public sealed class AppUser : IdentityUser<int>
{
    /// <summary>«Кандаулов В.М.» — показывается в интерфейсе.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Пароль выдан администратором — при входе нужно сменить на свой.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Заблокирован администратором (не путать с временной блокировкой после неудачных попыток).</summary>
    public bool IsBlocked => LockoutEnd == DateTimeOffset.MaxValue;
}
