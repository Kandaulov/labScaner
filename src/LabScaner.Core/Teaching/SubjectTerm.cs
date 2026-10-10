using LabScaner.Core.Abstractions;
using LabScaner.Core.Directory;
using LabScaner.Core.Subjects;

namespace LabScaner.Core.Teaching;

/// <summary>
/// Предмет в семестре: «КорпИС, 2026-2027, 7 сем» — группы, корневая папка на Диске, работы,
/// темы курсовых (ADR-011…013, ADR-025). Принадлежит преподавателю предмета.
/// </summary>
public sealed class SubjectTerm : ITeacherOwned
{
    public const int MaxLabs = 30;

    private readonly List<Group> _groups = [];
    private readonly List<Assignment> _assignments = [];
    private readonly List<CourseworkTopic> _topics = [];

    private SubjectTerm()
    {
        DiskRootPath = string.Empty;
    }

    public SubjectTerm(Subject subject, Term term, int studySemester, string diskRootPath)
        : this()
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(term);
        Subject = subject;
        Term = term;
        SetStudySemester(studySemester);
        SetDiskRootPath(diskRootPath);
    }

    public int Id { get; private set; }

    public int TeacherId { get; private set; }

    public int SubjectId { get; private set; }

    public Subject? Subject { get; private set; }

    public int TermId { get; private set; }

    public Term? Term { get; private set; }

    /// <summary>Номер семестра обучения: 7.</summary>
    public int StudySemester { get; private set; }

    /// <summary>Корневая папка на Диске: «30 Политех/03 КорпИС/…/2026-2027 КорпИС ИСТ - 7 сем».</summary>
    public string DiskRootPath { get; private set; }

    public IReadOnlyCollection<Group> Groups => _groups;

    public IReadOnlyCollection<Assignment> Assignments => _assignments;

    public IReadOnlyCollection<CourseworkTopic> Topics => _topics;

    public IEnumerable<Assignment> Labs => _assignments.Where(a => a.Kind == AssignmentKind.Lab).OrderBy(a => a.Number);

    public Assignment? Coursework => _assignments.FirstOrDefault(a => a.Kind == AssignmentKind.Coursework);

    /// <summary>«КорпИС, 2026-2027, осенний · 7 сем».</summary>
    public string Title => $"{Subject?.Code}, {Term?.Title} · {StudySemester} сем";

    /// <summary>Путь на Диске, который предлагается по шаблону предмета.</summary>
    public static string SuggestPath(Subject subject, Term term, int studySemester, string direction)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(term);
        return DiskPathTemplate.Render(subject.DiskPathTemplate, new DiskPathValues(subject.Code, term.AcademicYear, direction, studySemester));
    }

    public void SetStudySemester(int studySemester)
    {
        if (studySemester is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(studySemester), studySemester, "Номер семестра обучения — от 1 до 12.");
        }

        StudySemester = studySemester;
    }

    public void SetDiskRootPath(string path)
    {
        var normalized = DiskPathTemplate.Normalize(path ?? string.Empty);
        if (normalized.IndexOfAny(['{', '}']) >= 0)
        {
            throw new ArgumentException("В пути остались подстановки в фигурных скобках — укажите настоящий путь.", nameof(path));
        }

        var error = DiskPathTemplate.Validate(normalized);
        if (error is not null)
        {
            throw new ArgumentException(error.Replace("Шаблон пути", "Путь", StringComparison.Ordinal), nameof(path));
        }

        DiskRootPath = normalized;
    }

    public void AddGroup(Group group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (_groups.Any(g => g.Id == group.Id && group.Id != 0) || _groups.Contains(group))
        {
            return;
        }

        _groups.Add(group);
    }

    public void RemoveGroup(Group group) => _groups.Remove(group);

    /// <summary>Добавить лабораторные до <paramref name="count"/> штук: Лаб №1…№N.</summary>
    public void EnsureLabs(int count)
    {
        if (count is < 0 or > MaxLabs)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, $"Лабораторных — от 0 до {MaxLabs}.");
        }

        for (var n = Labs.Count() + 1; n <= count; n++)
        {
            _assignments.Add(new Assignment(this, AssignmentKind.Lab, n, null));
        }
    }

    public Assignment AddLab(string? title = null)
    {
        var count = Labs.Count();
        if (count >= MaxLabs)
        {
            throw new InvalidOperationException($"Не больше {MaxLabs} лабораторных.");
        }

        var lab = new Assignment(this, AssignmentKind.Lab, count + 1, title);
        _assignments.Add(lab);
        return lab;
    }

    /// <summary>Удаляет последнюю лабораторную (номера идут подряд).</summary>
    public void RemoveLastLab()
    {
        var last = Labs.LastOrDefault();
        if (last is not null)
        {
            _assignments.Remove(last);
        }
    }

    public void SetCoursework(bool enabled, string? title = null)
    {
        var existing = Coursework;
        if (enabled && existing is null)
        {
            _assignments.Add(new Assignment(this, AssignmentKind.Coursework, 0, title));
        }
        else if (!enabled && existing is not null)
        {
            _assignments.Remove(existing);
        }
    }

    /// <summary>Тема курсовой студента; пустая строка удаляет тему.</summary>
    public void SetTopic(int studentId, string? topic)
    {
        var existing = _topics.FirstOrDefault(t => t.StudentId == studentId);
        if (string.IsNullOrWhiteSpace(topic))
        {
            if (existing is not null)
            {
                _topics.Remove(existing);
            }

            return;
        }

        if (topic.Trim().Length > 300)
        {
            throw new ArgumentException("Тема курсовой — не длиннее 300 символов.", nameof(topic));
        }

        if (existing is null)
        {
            _topics.Add(new CourseworkTopic(this, studentId, topic));
        }
        else
        {
            existing.Change(topic);
        }
    }

    public string? TopicOf(int studentId) => _topics.FirstOrDefault(t => t.StudentId == studentId)?.Topic;

    public void AssignTeacher(int teacherId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(teacherId);
        if (TeacherId != 0 && TeacherId != teacherId)
        {
            throw new InvalidOperationException("Предмет в семестре принадлежит другому преподавателю.");
        }

        TeacherId = teacherId;
    }
}
