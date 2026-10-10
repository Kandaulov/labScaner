using System.Text;
using System.Text.RegularExpressions;

namespace LabScaner.Core.Subjects;

/// <summary>Значения для подстановки в шаблон пути (ADR-011).</summary>
public sealed record DiskPathValues(string Code, string AcademicYear, string Direction, int SemesterNumber);

/// <summary>
/// Шаблон пути к папке предмета в семестре на Яндекс Диске, например
/// <c>30 Политех/03 {Код}/10 {Код} - Отчетные документы/{Учебный год} {Код} {Направление} - {N} сем</c>.
/// Система предлагает путь по шаблону, преподаватель может поправить его у конкретного семестра.
/// </summary>
public static partial class DiskPathTemplate
{
    public const string Default = "{Код}/{Учебный год} {Код} {Направление} - {N} сем";

    /// <summary>Поддерживаемые подстановки и их описание для подсказки в интерфейсе.</summary>
    public static IReadOnlyDictionary<string, string> Placeholders { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["{Код}"] = "код предмета: КорпИС",
        ["{Учебный год}"] = "2026-2027",
        ["{Направление}"] = "буквы группы: ИСТ",
        ["{N}"] = "номер семестра обучения: 7",
    };

    /// <summary>Приводит шаблон к единому виду: «/» вместо «\», без пустых сегментов и пробелов по краям.</summary>
    public static string Normalize(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var segments = template.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('/', segments);
    }

    /// <summary>Ошибка в шаблоне или <c>null</c>.</summary>
    public static string? Validate(string template)
    {
        var normalized = Normalize(template);
        if (normalized.Length == 0)
        {
            return "Шаблон пути пуст.";
        }

        var unknown = Placeholder().Matches(normalized).Select(m => m.Value).Where(p => !Placeholders.ContainsKey(p)).Distinct().ToList();
        if (unknown.Count > 0)
        {
            return $"Неизвестные подстановки: {string.Join(", ", unknown)}. Доступны: {string.Join(", ", Placeholders.Keys)}.";
        }

        if (normalized.Split('/').Any(s => s is "." or ".."))
        {
            return "Сегменты «.» и «..» в пути недопустимы.";
        }

        return normalized.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0
            ? "Путь содержит символы, недопустимые в именах папок: < > : \" | ? *"
            : null;
    }

    public static string Render(string template, DiskPathValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = new StringBuilder(Normalize(template))
            .Replace("{Код}", values.Code)
            .Replace("{Учебный год}", values.AcademicYear)
            .Replace("{Направление}", values.Direction)
            .Replace("{N}", values.SemesterNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return result.ToString();
    }

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Placeholder();
}
