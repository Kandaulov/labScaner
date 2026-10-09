namespace LabScaner.Core.Directory;

/// <summary>Сравнение ФИО студентов: регистр не важен, «ё» = «е» (ADR-010).</summary>
public static class StudentNames
{
    public static string Normalize(string value) =>
        value.Trim().ToUpperInvariant().Replace('Ё', 'Е');

    public static string Key(PersonName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Normalize(name.FullName);
    }

    public static bool SameLastAndFirst(PersonName a, PersonName b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return Normalize(a.LastName) == Normalize(b.LastName) && Normalize(a.FirstName) == Normalize(b.FirstName);
    }

    /// <summary>
    /// Один и тот же студент: полное совпадение ФИО или совпадение фамилии и имени, если у одного из
    /// двух нет отчества («Иванов Иван» и «Иванов Иван Иванович»).
    /// </summary>
    public static bool Same(PersonName a, PersonName b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (!SameLastAndFirst(a, b))
        {
            return false;
        }

        return a.MiddleName is null || b.MiddleName is null || Normalize(a.MiddleName) == Normalize(b.MiddleName);
    }
}
