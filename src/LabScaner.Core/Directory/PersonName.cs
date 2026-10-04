using System.Globalization;

namespace LabScaner.Core.Directory;

/// <summary>ФИО студента. Отчество может отсутствовать.</summary>
public sealed record PersonName(string LastName, string FirstName, string? MiddleName)
{
    private static readonly CultureInfo _ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>
    /// Разбор «Фамилия Имя [Отчество]» в любом регистре: «ИВАНОВ ИВАН ИВАНОВИЧ» → «Иванов Иван Иванович».
    /// Двойные фамилии через дефис сохраняются: «петрова-водкина» → «Петрова-Водкина».
    /// </summary>
    public static PersonName Parse(string fullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        var parts = fullName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            throw new FormatException($"Ожидается «Фамилия Имя [Отчество]», получено «{fullName}».");
        }

        var middle = parts.Length > 2 ? string.Join(' ', parts[2..].Select(Capitalize)) : null;
        return new PersonName(Capitalize(parts[0]), Capitalize(parts[1]), middle);
    }

    /// <summary>«Иванов Иван Иванович».</summary>
    public string FullName => MiddleName is null ? $"{LastName} {FirstName}" : $"{LastName} {FirstName} {MiddleName}";

    /// <summary>«Иванов И.И.» — для имени файла на Диске (ADR-011).</summary>
    public string WithInitials => MiddleName is null
        ? $"{LastName} {FirstName[0]}."
        : $"{LastName} {FirstName[0]}.{MiddleName[0]}.";

    private static string Capitalize(string word) =>
        string.Join('-', word.Split('-').Select(p => _ru.TextInfo.ToTitleCase(p.ToLower(_ru))));
}
