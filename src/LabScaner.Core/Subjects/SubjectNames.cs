namespace LabScaner.Core.Subjects;

/// <summary>
/// Сравнение кода предмета с тем, что написал студент в теме письма (ADR-010):
/// без регистра, «ё» = «е», без пробелов, точек, дефисов. «Корп ИС», «корпис», «КОРП-ИС» — одно и то же.
/// </summary>
public static class SubjectNames
{
    public static string Key(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch is '.' or '-' or '–' or '—' or '_')
            {
                continue;
            }

            buffer[length++] = ch is 'ё' or 'Ё' ? 'Е' : char.ToUpperInvariant(ch);
        }

        return new string(buffer[..length]);
    }
}
