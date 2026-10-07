namespace LabScaner.Web.Navigation;

/// <summary>Пункт бокового меню (макет «Меню labScaner»).</summary>
public sealed record NavItem(string Id, string Label, string Href, string? SectionLabel = null);

public static class NavMenu
{
    public const string Summary = "summary";
    public const string Journal = "journal";
    public const string Processing = "processing";
    public const string Unrecognized = "unrecognized";
    public const string Notifications = "notifications";
    public const string Events = "events";
    public const string Subjects = "subjects";
    public const string Groups = "groups";
    public const string Settings = "settings";

    /// <summary>ViewData-ключ, которым страница отмечает активный пункт меню.</summary>
    public const string ActiveKey = "Nav";

    public static IReadOnlyList<NavItem> Items { get; } =
    [
        new(Summary, "Сводка", "/"),
        new(Journal, "Журнал группы", "/Journal"),
        new(Processing, "Обработка", "/Processing"),
        new(Unrecognized, "Нераспознанные", "/Unrecognized"),
        new(Notifications, "Уведомления", "/Notifications"),
        new(Events, "Журнал событий", "/Events"),
        new(Subjects, "Предметы", "/Subjects", SectionLabel: "Справочники"),
        new(Groups, "Группы и студенты", "/Groups"),
        new(Settings, "Настройки", "/Settings"),
    ];

    /// <summary>Инициалы для аватара: «Кандаулов В.М.» → «КВ».</summary>
    public static string Initials(string displayName)
    {
        var letters = displayName
            .Split([' ', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(p => char.IsLetter(p[0]))
            .Take(2)
            .Select(p => char.ToUpperInvariant(p[0]));
        var result = string.Concat(letters);
        return result.Length > 0 ? result : "?";
    }
}

/// <summary>Пустое состояние раздела: что здесь будет и что сделать.</summary>
public sealed record EmptyState(string Title, string Text, string? LinkText = null, string? LinkHref = null);
