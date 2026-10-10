using System.Text;
using System.Text.RegularExpressions;

namespace LabScaner.Core.Teaching;

/// <summary>
/// Абзац документа с заданиями — то, что нужно для деления: текст, уровень заголовка
/// (0 — обычный абзац), жирный ли он целиком, уровень списка (-1 — не элемент списка).
/// </summary>
public sealed record DocParagraph(string Text, int HeadingLevel = 0, bool IsBold = false, int ListLevel = -1);

/// <summary>Задание одной лабораторной, найденное в документе.</summary>
public sealed record LabTask(int Number, string Heading, string Title, string Text, IReadOnlyList<string> Checklist);

/// <summary>Результат деления документа: общие требования, лабораторные, предупреждения для преподавателя.</summary>
public sealed record TaskSplit(string Preamble, IReadOnlyList<LabTask> Labs, IReadOnlyList<string> Warnings);

/// <summary>Задание на курсовую: весь текст документа и черновик чек-листа из раздела «Критерии оценки».</summary>
public sealed record CourseworkTask(string Text, IReadOnlyList<string> Checklist, IReadOnlyList<string> Warnings);

/// <summary>
/// Делит DOCX «Задания на лабораторные» по заголовкам «Лабораторная работа №N» (ADR-012, ADR-029).
/// Заголовком считается абзац, который начинается с этих слов: сначала ищутся абзацы со стилем заголовка,
/// если их нет — жирные, если нет и их — короткие отдельные абзацы. Ссылки в тексте («из работы №1»)
/// заголовками не считаются.
/// </summary>
public static partial class TaskDocumentSplitter
{
    public const int MaxTitleLength = 150;
    public const int MaxChecklistItems = 20;
    public const int MaxChecklistItemLength = 300;

    /// <summary>Задание короче — предупреждение: ИИ будет не на что опереться.</summary>
    public const int ShortTaskLength = 300;

    public static TaskSplit Split(IReadOnlyList<DocParagraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var warnings = new List<string>();
        var candidates = paragraphs.Select((p, i) => (Index: i, Paragraph: p, Match: LabHeading().Match(Clean(p.Text))))
            .Where(c => c.Match.Success && c.Paragraph.ListLevel < 0)
            .ToList();

        var headings = candidates.Where(c => c.Paragraph.HeadingLevel > 0).ToList();
        if (headings.Count == 0)
        {
            headings = [.. candidates.Where(c => c.Paragraph.IsBold)];
        }

        if (headings.Count == 0)
        {
            headings = [.. candidates.Where(c => Clean(c.Paragraph.Text).Length <= 80)];
        }

        if (headings.Count == 0)
        {
            return new TaskSplit(Render(paragraphs), [], ["В документе не найдено ни одного заголовка «Лабораторная работа №N»."]);
        }

        var labLevel = headings[0].Paragraph.HeadingLevel;
        var labs = new List<LabTask>();
        var extra = new StringBuilder();
        for (var h = 0; h < headings.Count; h++)
        {
            var start = headings[h].Index + 1;
            var end = h + 1 < headings.Count ? headings[h + 1].Index : paragraphs.Count;

            // Другой заголовок того же уровня («Литература», «Курсовая работа») заканчивает лабораторную.
            var body = new List<DocParagraph>();
            for (var i = start; i < end; i++)
            {
                var p = paragraphs[i];
                if (labLevel > 0 && p.HeadingLevel is > 0 && p.HeadingLevel <= labLevel)
                {
                    var rest = paragraphs.Skip(i).Take(end - i).ToList();
                    extra.AppendLine().Append(Render(rest));
                    warnings.Add($"Раздел «{Clean(p.Text)}» после лабораторной №{headings[h].Match.Groups["n"].Value} не относится ни к одной работе — добавлен к общим требованиям.");
                    break;
                }

                body.Add(p);
            }

            var number = int.Parse(headings[h].Match.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (labs.Any(l => l.Number == number))
            {
                warnings.Add($"Лабораторная №{number} встречается в документе дважды — взята первая.");
                continue;
            }

            var heading = Clean(headings[h].Paragraph.Text);
            var title = TitleOf(headings[h].Match.Groups["tail"].Value, body);
            var text = Render(body);
            labs.Add(new LabTask(number, heading, title, text, ChecklistOf(body)));

            if (title.Length == 0)
            {
                warnings.Add($"У лабораторной №{number} нет названия (ни в заголовке, ни в разделе «Цель») — впишите его.");
            }

            if (text.Length < ShortTaskLength)
            {
                warnings.Add($"Задание лабораторной №{number} короткое ({text.Length} знаков) — проверка ИИ по нему будет неточной.");
            }
        }

        labs.Sort((a, b) => a.Number.CompareTo(b.Number));
        if (labs.Count > 0)
        {
            var missing = Enumerable.Range(1, labs[^1].Number).Except(labs.Select(l => l.Number)).ToList();
            if (missing.Count > 0)
            {
                warnings.Add($"В документе нет лабораторных №{string.Join(", №", missing)} — их задания останутся прежними.");
            }
        }

        var preamble = Render(paragraphs.Take(headings[0].Index).ToList()) + extra;
        return new TaskSplit(preamble.Trim(), labs, warnings);
    }

    /// <summary>
    /// Курсовая не делится: задание — весь документ. Чек-лист — из раздела «Критерии оценки», если он есть.
    /// </summary>
    public static CourseworkTask ParseCoursework(IReadOnlyList<DocParagraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var text = Render(paragraphs);
        var warnings = new List<string>();
        if (text.Length < ShortTaskLength)
        {
            warnings.Add($"Задание на курсовую короткое ({text.Length} знаков) — проверка ИИ по нему будет неточной.");
        }

        var start = paragraphs.ToList().FindIndex(p => p.ListLevel < 0 && Criteria().IsMatch(Clean(p.Text)) && Clean(p.Text).Length <= 60);
        if (start < 0)
        {
            warnings.Add("В документе нет раздела «Критерии оценки» — чек-лист курсовой заполните вручную.");
            return new CourseworkTask(text, [], warnings);
        }

        var level = paragraphs[start].HeadingLevel;
        var section = new List<DocParagraph>();
        foreach (var p in paragraphs.Skip(start + 1))
        {
            if (p.HeadingLevel > 0 && (level == 0 || p.HeadingLevel <= level))
            {
                break;
            }

            section.Add(p);
        }

        return new CourseworkTask(text, ChecklistOf(section), warnings);
    }

    /// <summary>Текст задания для ИИ и для правки: подзаголовки с пустой строкой перед ними, списки — «- » с отступом.</summary>
    public static string Render(IReadOnlyList<DocParagraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        var sb = new StringBuilder();
        foreach (var p in paragraphs)
        {
            var text = Clean(p.Text);
            if (text.Length == 0)
            {
                continue;
            }

            if (p.HeadingLevel > 0 && sb.Length > 0)
            {
                sb.AppendLine();
            }

            if (p.ListLevel >= 0)
            {
                sb.Append(' ', p.ListLevel * 2).Append("- ");
            }

            sb.AppendLine(text);
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Название: хвост заголовка («Лабораторная работа №2. Сервис загрузки») или первое предложение раздела «Цель».
    /// </summary>
    private static string TitleOf(string tail, List<DocParagraph> body)
    {
        var title = tail.Trim().Trim('.', ':', '«', '»', '"', ' ', '-', '–', '—');
        if (title.Length == 0)
        {
            var goal = body.FindIndex(p => Goal().IsMatch(Clean(p.Text)));
            if (goal >= 0)
            {
                var inline = Goal().Match(Clean(body[goal].Text)).Groups["rest"].Value.Trim();
                title = inline.Length > 0
                    ? inline
                    : body.Skip(goal + 1).Select(p => Clean(p.Text)).FirstOrDefault(t => t.Length > 0) ?? string.Empty;
            }
        }

        title = FirstSentence(title);
        return title.Length <= MaxTitleLength ? title : title[..(MaxTitleLength - 1)].TrimEnd() + "…";
    }

    /// <summary>
    /// Черновик чек-листа: элементы списков (вложенные — через «;» к родителю) и самостоятельные абзацы
    /// всех разделов, кроме «Цели»; содержимое раздела «Отчет по работе» — с пометкой «В отчёте:».
    /// Вводные фразы с двоеточием в конце пропускаются. Преподаватель правит черновик перед проверками.
    /// </summary>
    private static List<string> ChecklistOf(List<DocParagraph> body)
    {
        var items = new List<string>();
        var section = Section.Other;
        string? parent = null;
        var children = new List<string>();

        void Flush()
        {
            if (parent is not null)
            {
                var joined = children.Count == 0 ? parent : $"{parent.TrimEnd(':', ' ')}: {string.Join("; ", children)}";
                if (joined.Length <= MaxChecklistItemLength)
                {
                    items.Add(joined);
                }
                else
                {
                    // Длинный пункт с подпунктами — каждый подпункт отдельной строкой.
                    items.Add(parent);
                    items.AddRange(children);
                }
            }

            parent = null;
            children.Clear();
        }

        void Add(string text)
        {
            Flush();
            if (!text.EndsWith(':'))
            {
                items.Add(section == Section.Report ? "В отчёте: " + text : text);
            }
        }

        foreach (var p in body)
        {
            var text = Clean(p.Text);
            if (text.Length == 0)
            {
                continue;
            }

            var goal = Goal().Match(text);
            if (p.ListLevel < 0 && goal.Success)
            {
                Flush();
                section = Section.Goal;
                continue;
            }

            var report = Report().Match(text);
            if (p.ListLevel < 0 && report.Success)
            {
                Flush();
                section = Section.Report;
                var rest = report.Groups["rest"].Value.Trim();
                if (rest.Length > 0)
                {
                    Add(rest.TrimEnd('.'));
                }

                continue;
            }

            if (p.HeadingLevel > 0 || (p.ListLevel < 0 && text.Length <= 60 && text.EndsWith(':')))
            {
                Flush();
                section = p.HeadingLevel > 0 ? Section.Other : section;
                continue;
            }

            if (section == Section.Goal)
            {
                continue;
            }

            if (p.ListLevel == 0 || (p.ListLevel > 0 && parent is null))
            {
                Flush();
                parent = text.TrimEnd('.', ';');
            }
            else if (p.ListLevel > 0)
            {
                children.Add(text.TrimEnd('.', ';'));
            }
            else
            {
                Add(text.TrimEnd('.'));
            }
        }

        Flush();
        return [.. items.Where(i => i.Count(char.IsLetter) >= 3).Take(MaxChecklistItems).Select(i => i.Length <= MaxChecklistItemLength ? i : i[..(MaxChecklistItemLength - 1)] + "…")];
    }

    private enum Section
    {
        Other,
        Goal,
        Report,
    }

    private static string FirstSentence(string text)
    {
        var match = SentenceEnd().Match(text);
        return (match.Success ? text[..match.Index] : text).Trim().TrimEnd('.');
    }

    private static string Clean(string text) => Whitespace().Replace(text.Replace(' ', ' '), " ").Trim();

    [GeneratedRegex(@"^лабораторн\w*\s+работ\w*\s*(?:№|N|#|No\.?)?\s*(?<n>\d{1,2})(?!\d)\s*[.:)\-–—]?\s*(?<tail>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LabHeading();

    [GeneratedRegex(@"^цел[ьи](?:\s+работы)?\s*(?::\s*(?<rest>.*)|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Goal();

    [GeneratedRegex(@"^(?:содержание\s+)?отч[её]т(?:\s+по\s+(?:лабораторной\s+)?работе)?\s*(?::\s*(?<rest>.*)|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Report();

    [GeneratedRegex(@"^критери[йи]\w*\s+(?:оценк|оценив)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Criteria();

    [GeneratedRegex(@"[.!?](\s|$)")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
