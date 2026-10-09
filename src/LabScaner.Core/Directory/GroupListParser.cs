namespace LabScaner.Core.Directory;

/// <summary>Строка списка группы после разбора.</summary>
public sealed record GroupListRow(int RowNumber, string RawName, PersonName? Name, string? Topic, string? Email, string? Error);

/// <summary>Лист списка = группа.</summary>
public sealed record GroupListSheet(string SheetName, string? GroupName, IReadOnlyList<GroupListRow> Rows, string? Error)
{
    public IEnumerable<GroupListRow> ValidRows => Rows.Where(r => r.Error is null);
}

/// <summary>
/// Разбор списка группы из таблицы (формат — <c>tests/fixtures/groups/</c>): лист на группу, имя листа —
/// название группы («ИСТ41» → «ИСТ-41»), колонки «№», «ФИО», «Тема КР», при наличии — «E-mail».
/// Не зависит от формата файла: на вход — уже прочитанные ячейки.
/// </summary>
public static class GroupListParser
{
    private static readonly string[] _nameHeaders = ["ФИО", "СТУДЕНТ", "Ф.И.О."];
    private static readonly string[] _topicHeaders = ["ТЕМА"];
    private static readonly string[] _emailHeaders = ["E-MAIL", "EMAIL", "ПОЧТА", "ЭЛ. ПОЧТА", "ЭЛЕКТРОННАЯ ПОЧТА"];

    /// <param name="rows">Строки листа: индекс строки — номер строки в файле минус 1, ячейки — по столбцам A, B, C…</param>
    public static GroupListSheet Parse(string sheetName, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var groupName = TryGroupName(sheetName);
        if (groupName is null)
        {
            return new GroupListSheet(sheetName, null, [], $"Имя листа «{sheetName}» не похоже на название группы (ожидается, например, «ИСТ41» или «ИСТ-41»).");
        }

        var headerIndex = -1;
        int nameCol = -1, topicCol = -1, emailCol = -1;
        for (var i = 0; i < Math.Min(rows.Count, 10) && headerIndex < 0; i++)
        {
            var cells = rows[i];
            for (var c = 0; c < cells.Count; c++)
            {
                var text = cells[c]?.Trim().ToUpperInvariant() ?? string.Empty;
                if (nameCol < 0 && _nameHeaders.Any(h => text == h || text.StartsWith(h + " ", StringComparison.Ordinal)))
                {
                    nameCol = c;
                    headerIndex = i;
                }
            }

            if (headerIndex >= 0)
            {
                for (var c = 0; c < cells.Count; c++)
                {
                    var text = cells[c]?.Trim().ToUpperInvariant() ?? string.Empty;
                    if (topicCol < 0 && _topicHeaders.Any(h => text.StartsWith(h, StringComparison.Ordinal)))
                    {
                        topicCol = c;
                    }
                    else if (emailCol < 0 && _emailHeaders.Any(h => text == h))
                    {
                        emailCol = c;
                    }
                }
            }
        }

        if (headerIndex < 0)
        {
            return new GroupListSheet(sheetName, groupName, [], "Не найден заголовок столбца «ФИО» в первых 10 строках.");
        }

        var result = new List<GroupListRow>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = headerIndex + 1; i < rows.Count; i++)
        {
            var cells = rows[i];
            var raw = Cell(cells, nameCol);
            var topic = Cell(cells, topicCol);
            var email = Cell(cells, emailCol);
            if (raw is null)
            {
                if (topic is not null || email is not null)
                {
                    result.Add(new GroupListRow(i + 1, string.Empty, null, topic, email, "Пустое ФИО"));
                }

                continue;
            }

            result.Add(ParseRow(i + 1, raw, topic, email, seen));
        }

        return new GroupListSheet(sheetName, groupName, result, result.Count == 0 ? "На листе нет студентов." : null);
    }

    /// <summary>Название группы из имени листа или <c>null</c>, если не похоже на группу.</summary>
    public static string? TryGroupName(string sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName))
        {
            return null;
        }

        var normalized = GroupName.Normalize(sheetName);
        var dash = normalized.IndexOf('-', StringComparison.Ordinal);
        if (dash <= 0 || dash == normalized.Length - 1)
        {
            return null;
        }

        var letters = normalized[..dash];
        var number = normalized[(dash + 1)..];
        return letters.All(char.IsLetter) && number.All(char.IsLetterOrDigit) && char.IsDigit(number[0]) ? normalized : null;
    }

    private static GroupListRow ParseRow(int rowNumber, string raw, string? topic, string? email, Dictionary<string, int> seen)
    {
        if (raw.Any(ch => char.IsDigit(ch) || ch is '@' or '[' or ']' or '?'))
        {
            return new GroupListRow(rowNumber, raw, null, topic, email, "ФИО содержит недопустимые символы");
        }

        PersonName name;
        try
        {
            name = PersonName.Parse(raw);
        }
        catch (FormatException)
        {
            return new GroupListRow(rowNumber, raw, null, topic, email, "Нужны хотя бы фамилия и имя");
        }

        string? normalizedEmail = null;
        if (email is not null)
        {
            try
            {
                normalizedEmail = StudentEmail.Normalize(email);
            }
            catch (FormatException)
            {
                return new GroupListRow(rowNumber, raw, name, topic, email, $"Некорректный e-mail «{email}»");
            }
        }

        var key = StudentNames.Key(name);
        if (seen.TryGetValue(key, out var firstRow))
        {
            return new GroupListRow(rowNumber, raw, name, topic, normalizedEmail, $"Повтор строки {firstRow}");
        }

        seen[key] = rowNumber;
        return new GroupListRow(rowNumber, raw, name, topic, normalizedEmail, null);
    }

    private static string? Cell(IReadOnlyList<string?> cells, int index)
    {
        if (index < 0 || index >= cells.Count)
        {
            return null;
        }

        var value = cells[index]?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
