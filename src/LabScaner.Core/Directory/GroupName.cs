using System.Text;

namespace LabScaner.Core.Directory;

/// <summary>
/// Приведение названия группы к каноническому виду «ИСТ-41».
/// Встречается: «ИСТ41» (имя листа в списке группы), «ист-41», «ИСТ – 41» (тема письма), «ИСТ_41».
/// </summary>
public static class GroupName
{
    private static readonly char[] _dashes = ['-', '–', '—', '‐', '−', '_'];

    /// <summary>
    /// Каноническое написание: буквы направления, дефис, номер; регистр букв сохраняется («ИСТбд-41»).
    /// Для сравнения названий использовать <see cref="Key"/>.
    /// </summary>
    public static string Normalize(string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);

        var compact = new StringBuilder(raw.Length);
        foreach (var ch in raw.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            compact.Append(Array.IndexOf(_dashes, ch) >= 0 ? '-' : ch);
        }

        var value = compact.ToString();
        var firstDigit = value.AsSpan().IndexOfAnyInRange('0', '9');
        if (firstDigit <= 0)
        {
            return value;
        }

        var prefix = value[..firstDigit].TrimEnd('-');
        var suffix = value[firstDigit..];

        // «ист-41», набранное строчными, — это «ИСТ-41»; смешанный регистр («ИСТбд») сохраняется.
        if (prefix.All(ch => !char.IsLetter(ch) || char.IsLower(ch)))
        {
            prefix = prefix.ToUpperInvariant();
        }

        return $"{prefix}-{suffix}";
    }

    /// <summary>Ключ для сравнения без учёта регистра: «ист-41», «ИСТ41» → «ИСТ-41».</summary>
    public static string Key(string raw) => Normalize(raw).ToUpperInvariant();

    /// <summary>Направление — буквенная часть до номера: «ИСТ-41» → «ИСТ».</summary>
    public static string DirectionOf(string normalizedName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName);
        var dash = normalizedName.IndexOf('-', StringComparison.Ordinal);
        return dash > 0 ? normalizedName[..dash] : normalizedName;
    }
}
