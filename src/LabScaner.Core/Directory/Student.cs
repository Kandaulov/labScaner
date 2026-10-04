namespace LabScaner.Core.Directory;

/// <summary>Студент группы. Отчисленный или переведённый — <see cref="IsActive"/> = false, запись не удаляется.</summary>
public sealed class Student
{
    private readonly List<StudentEmail> _emails = [];

    private Student()
    {
        LastName = string.Empty;
        FirstName = string.Empty;
    }

    internal Student(Group group, PersonName name)
    {
        Group = group;
        LastName = name.LastName;
        FirstName = name.FirstName;
        MiddleName = name.MiddleName;
        IsActive = true;
    }

    public int Id { get; private set; }

    public int GroupId { get; private set; }

    public Group? Group { get; private set; }

    public string LastName { get; private set; }

    public string FirstName { get; private set; }

    public string? MiddleName { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<StudentEmail> Emails => _emails;

    public PersonName Name => new(LastName, FirstName, MiddleName);

    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Запоминает адрес отправителя за студентом (ADR-024). Повторный адрес не дублируется;
    /// источник «Manual» имеет приоритет над «Auto».
    /// </summary>
    public StudentEmail AddEmail(string email, EmailSource source, DateTimeOffset now)
    {
        var normalized = StudentEmail.Normalize(email);
        var existing = _emails.FirstOrDefault(e => e.Email == normalized);
        if (existing is not null)
        {
            if (source == EmailSource.Manual)
            {
                existing.ConfirmManually();
            }

            return existing;
        }

        var added = new StudentEmail(normalized, source, now);
        _emails.Add(added);
        return added;
    }
}
