namespace LabScaner.Core.Directory;

/// <summary>Учебная группа. Общий справочник для всех преподавателей (ADR-016).</summary>
public sealed class Group
{
    private readonly List<Student> _students = [];

    private Group()
    {
        Name = string.Empty;
        NameKey = string.Empty;
        Direction = string.Empty;
    }

    public Group(string name, int? admissionYear = null)
    {
        Name = GroupName.Normalize(name);
        NameKey = GroupName.Key(name);
        Direction = GroupName.DirectionOf(Name);
        AdmissionYear = admissionYear;
    }

    public int Id { get; private set; }

    /// <summary>Каноническое название: «ИСТ-41».</summary>
    public string Name { get; private set; }

    /// <summary>Ключ уникальности и поиска без учёта регистра: «ИСТ-41».</summary>
    public string NameKey { get; private set; }

    /// <summary>Направление подготовки: «ИСТ».</summary>
    public string Direction { get; private set; }

    public int? AdmissionYear { get; private set; }

    public IReadOnlyCollection<Student> Students => _students;

    public Student AddStudent(PersonName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var student = new Student(this, name);
        _students.Add(student);
        return student;
    }
}
