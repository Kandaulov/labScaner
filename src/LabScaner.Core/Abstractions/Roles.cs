namespace LabScaner.Core.Abstractions;

/// <summary>Роли пользователей (ADR-016). Администратор — тоже преподаватель, у него обе роли.</summary>
public static class Roles
{
    public const string Teacher = "Teacher";

    public const string Admin = "Admin";

    public static string Title(string role) => role switch
    {
        Teacher => "Преподаватель",
        Admin => "Администратор",
        _ => role,
    };
}
