using LabScaner.Core.Abstractions;

namespace LabScaner.Core.Subjects;

/// <summary>
/// Предмет преподавателя: КорпИС, ОС, СПП (ADR-010, ADR-011, ADR-016).
/// Код и допустимые написания нужны, чтобы узнать предмет в теме письма.
/// </summary>
public sealed class Subject : ITeacherOwned
{
    public const int MaxAliases = 10;

    private List<string> _aliases = [];

    private Subject()
    {
        Name = string.Empty;
        Code = string.Empty;
        DiskPathTemplate = Subjects.DiskPathTemplate.Default;
    }

    public Subject(string name, string code)
        : this()
    {
        Update(name, code, [], FinalAssessment.Exam, null, Subjects.DiskPathTemplate.Default);
        IsActive = true;
    }

    public int Id { get; private set; }

    public int TeacherId { get; private set; }

    /// <summary>Полное название: «Корпоративные информационные системы».</summary>
    public string Name { get; private set; }

    /// <summary>Код из темы письма: «КорпИС».</summary>
    public string Code { get; private set; }

    /// <summary>Другие написания в теме письма: «КИС», «Корп ИС».</summary>
    public IReadOnlyList<string> Aliases => _aliases;

    public FinalAssessment FinalAssessment { get; private set; }

    /// <summary>Справочные материалы для ИИ: уходят в каждую проверку по предмету (ADR-012), без персональных данных.</summary>
    public string? AiReferenceText { get; private set; }

    /// <summary>Шаблон пути к папке семестра на Яндекс Диске (ADR-011).</summary>
    public string DiskPathTemplate { get; private set; }

    /// <summary>В архиве — предмет больше не читается; письма по нему не распознаются, данные сохраняются.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Все написания предмета для распознавания темы письма: код и алиасы.</summary>
    public IEnumerable<string> Tokens => Aliases.Prepend(Code);

    public bool Matches(string text) =>
        !string.IsNullOrWhiteSpace(text) && Tokens.Any(t => SubjectNames.Key(t) == SubjectNames.Key(text));

    /// <exception cref="ArgumentException">Пустые обязательные поля, неверный шаблон пути, слишком много написаний.</exception>
    public void Update(string name, string code, IEnumerable<string> aliases, FinalAssessment finalAssessment, string? aiReferenceText, string diskPathTemplate)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Укажите название предмета.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Укажите код предмета — как студенты пишут его в теме письма.", nameof(code));
        }

        if (code.Trim().Length > 32 || code.Contains('–') || code.Contains('—'))
        {
            throw new ArgumentException("Код — короткое слово без тире, например «КорпИС».", nameof(code));
        }

        var codeKey = SubjectNames.Key(code);
        var cleanAliases = aliases
            .Select(a => a.Trim())
            .Where(a => a.Length > 0 && SubjectNames.Key(a).Length > 0 && SubjectNames.Key(a) != codeKey)
            .DistinctBy(SubjectNames.Key)
            .ToList();
        if (cleanAliases.Count > MaxAliases)
        {
            throw new ArgumentException($"Не больше {MaxAliases} других написаний.", nameof(aliases));
        }

        var templateError = Subjects.DiskPathTemplate.Validate(diskPathTemplate ?? string.Empty);
        if (templateError is not null)
        {
            throw new ArgumentException(templateError, nameof(diskPathTemplate));
        }

        Name = name.Trim();
        Code = code.Trim();
        _aliases = cleanAliases;
        FinalAssessment = finalAssessment;
        AiReferenceText = string.IsNullOrWhiteSpace(aiReferenceText) ? null : aiReferenceText.Trim();
        DiskPathTemplate = Subjects.DiskPathTemplate.Normalize(diskPathTemplate!);
    }

    public void Archive() => IsActive = false;

    public void Restore() => IsActive = true;

    /// <summary>Написание, которое совпадает с написанием другого предмета того же преподавателя, или <c>null</c>.</summary>
    public string? ConflictWith(IEnumerable<Subject> others)
    {
        ArgumentNullException.ThrowIfNull(others);
        foreach (var other in others.Where(o => !ReferenceEquals(o, this) && (o.Id == 0 || o.Id != Id)))
        {
            var clash = Tokens.FirstOrDefault(t => other.Matches(t));
            if (clash is not null)
            {
                return $"Написание «{clash}» уже используется предметом «{other.Code}» — в теме письма их не различить.";
            }
        }

        return null;
    }

    public void AssignTeacher(int teacherId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(teacherId);
        if (TeacherId != 0 && TeacherId != teacherId)
        {
            throw new InvalidOperationException("Нельзя передать предмет другому преподавателю.");
        }

        TeacherId = teacherId;
    }
}
