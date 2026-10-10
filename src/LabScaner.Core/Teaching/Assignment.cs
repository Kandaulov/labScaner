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
/// появятся при загрузке DOCX (шаг 3.5).
/// </summary>
public sealed class Assignment : ITeacherOwned
{
    private Assignment()
    {
        Title = string.Empty;
    }

    internal Assignment(SubjectTerm subjectTerm, AssignmentKind kind, int number, string? title)
    {
        SubjectTerm = subjectTerm;
        Kind = kind;
        Number = kind == AssignmentKind.Coursework ? 0 : number;
        Title = string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
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

    /// <summary>Свой дедлайн работы; иначе — из календаря семестра (ADR-013).</summary>
    public DateOnly? DeadlineOverride { get; private set; }

    /// <summary>«Лаб №3» или «Курсовая».</summary>
    public string ShortName => Kind == AssignmentKind.Coursework ? "Курсовая" : $"Лаб №{Number}";

    public DateOnly Deadline(Term term)
    {
        ArgumentNullException.ThrowIfNull(term);
        return DeadlineOverride ?? (Kind == AssignmentKind.Coursework ? term.SessionStart : term.CreditWeekStart);
    }

    public const int MaxTitleLength = 300;

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
