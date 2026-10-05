using LabScaner.Core.Abstractions;

namespace LabScaner.Core.Subjects;

/// <summary>
/// Предмет преподавателя: КорпИС, ОС, СПП. Пока только основные поля; алиасы для темы письма,
/// тип аттестации и шаблон пути на Диске появятся на этапе 3.
/// </summary>
public sealed class Subject : ITeacherOwned
{
    private Subject()
    {
        Name = string.Empty;
        Code = string.Empty;
    }

    public Subject(string name, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Name = name.Trim();
        Code = code.Trim();
        IsActive = true;
    }

    public int Id { get; private set; }

    public int TeacherId { get; private set; }

    /// <summary>Полное название: «Корпоративные информационные системы».</summary>
    public string Name { get; private set; }

    /// <summary>Код из темы письма: «КорпИС».</summary>
    public string Code { get; private set; }

    public bool IsActive { get; private set; }

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
