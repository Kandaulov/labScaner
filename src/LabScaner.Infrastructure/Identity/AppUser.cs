using Microsoft.AspNetCore.Identity;

namespace LabScaner.Infrastructure.Identity;

/// <summary>Пользователь системы — преподаватель. Входит по логину (<see cref="IdentityUser{TKey}.UserName"/>).</summary>
public sealed class AppUser : IdentityUser<int>
{
    /// <summary>«Кандаулов В.М.» — показывается в интерфейсе.</summary>
    public string DisplayName { get; set; } = string.Empty;
}
