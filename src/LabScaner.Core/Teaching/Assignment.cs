using LabScaner.Core.Abstractions;
using LabScaner.Core.Directory;

namespace LabScaner.Core.Teaching;

public enum AssignmentKind
{
    Lab,
    Coursework,
}

/// <summary>
/// Работа предмета в семестре: лабораторная №N или курсовая (ADR-012). Текст задания и чек-лист
/// берутся из DOCX с заданиями (ADR-029) и правятся вручную; вместе они уходят в проверку ИИ.
/// </summary>
public sealed class Assignment : ITeacherOwned
{
    public const int MaxTitleLength = 300;
    public const int MaxTaskTextLength = 30_000;
    public const int MaxChecklistItems = 30;
    public const int MaxChecklistItemLength = 300;

    private List<string> _checklist = [];

    private Assignment()
    {
        Title = string.Empty;
        TaskText = string.Empty;
    }

    internal Assignment(SubjectTerm subjectTerm, AssignmentKind kind, int number, string? title)
    {
        SubjectTerm = subjectTerm;
        Kind = kind;
        Number = kind == AssignmentKind.Coursework ? 0 : number;
        Title = string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
        TaskText = string.Empty;
        AiCheckEnabled = true;
    }

    public int Id { get; private set; }

    public int TeacherId { get; private set; }

    public int SubjectTermId { get; private set; }

    public SubjectTerm? SubjectTerm { get; private set; }

    public AssignmentKind Kind { get; private set; }

    /// <summary>Номер лабораторной; у курсовой — 0.</summary>
    public int Number { get; private set; }

    public string Title { get; private set; }

    public bool AiCheckEnabled { get; private set; }

    /// <summary>Текст задания — фрагмент DOCX по заголовку или весь DOCX курсовой.</summary>
    public string TaskText { get; private set; }

    /// <summary>Пункты, по которым ИИ проверяет работу.</summary>
    public IReadOnlyList<string> Checklist => _checklist;

    public bool HasTask => TaskText.Length > 0;

    /// <summary>Свой дедлайн работы; иначе — из календаря семестра (ADR-013).</summary>
    public DateOnly? DeadlineOverride { get; private set; }

    /// <summary>«Лаб №3» или «Курсовая».</summary>
    public string ShortName => Kind == AssignmentKind.Coursework ? "Курсовая" : $"Лаб №{Number}";

    public DateOnly Deadline(Term term)
    {
        ArgumentNullException.ThrowIfNull(term);
        return DeadlineOverride ?? (Kind == AssignmentKind.Coursework ? term.SessionStart : term.CreditWeekStart);
    }

    public void Update(string? title, DateOnly? deadlineOverride, bool aiCheckEnabled)
    {
        var trimmed = string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
        if (trimmed.Length > MaxTitleLength)
        {
            throw new ArgumentException($"Название работы — не длиннее {MaxTitleLength} символов.", nameof(title));
        }

        Title = trimmed;
        DeadlineOverride = deadlineOverride;
        AiCheckEnabled = aiCheckEnabled;
    }

    /// <summary>Задание и чек-лист: пустые пункты отбрасываются, повторы — тоже.</summary>
    public void SetTask(string? text, IEnumerable<string>? checklist)
    {
        var cleanText = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (cleanText.Length > MaxTaskTextLength)
        {
            throw new ArgumentException($"Текст задания — не длиннее {MaxTaskTextLength} символов.", nameof(text));
        }

        var items = (checklist ?? [])
            .Select(i => i.Trim().TrimStart('-', '•', '*', ' ').Trim())
            .Where(i => i.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (items.Count > MaxChecklistItems)
        {
            throw new ArgumentException($"В чек-листе — не больше {MaxChecklistItems} пунктов.", nameof(checklist));
        }

        if (items.Find(i => i.Length > MaxChecklistItemLength) is { } longItem)
        {
            throw new ArgumentException($"Пункт чек-листа длиннее {MaxChecklistItemLength} символов: «{longItem[..40]}…».", nameof(checklist));
        }

        TaskText = cleanText;
        _checklist = items;
    }

    /// <summary>Название, задание, чек-лист и проверка ИИ — из такой же работы другого семестра; дедлайн свой.</summary>
    internal void CopyFrom(Assignment source)
    {
        Title = source.Title;
        AiCheckEnabled = source.AiCheckEnabled;
        TaskText = source.TaskText;
        _checklist = [.. source._checklist];
    }

    public void AssignTeacher(int teacherId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(teacherId);
        if (TeacherId != 0 && TeacherId != teacherId)
        {
            throw new InvalidOperationException("Работа принадлежит другому преподавателю.");
        }

        TeacherId = teacherId;
    }
}
