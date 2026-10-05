namespace LabScaner.Core.Directory;

/// <summary>Откуда известен адрес студента (ADR-024).</summary>
public enum EmailSource
{
    /// <summary>Из файла списка группы.</summary>
    Import,

    /// <summary>Запомнен по распознанному письму.</summary>
    Auto,

    /// <summary>Подтверждён преподавателем при ручном сопоставлении.</summary>
    Manual,
}

public sealed class StudentEmail
{
    private StudentEmail()
    {
        Email = string.Empty;
    }

    internal StudentEmail(string normalizedEmail, EmailSource source, DateTimeOffset createdAt)
    {
        Email = normalizedEmail;
        Source = source;
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }

    public int StudentId { get; private set; }

    /// <summary>Адрес в нижнем регистре, без пробелов.</summary>
    public string Email { get; private set; }

    public EmailSource Source { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal void ConfirmManually() => Source = EmailSource.Manual;

    public static string Normalize(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var value = email.Trim().ToLowerInvariant();
        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
        {
            throw new FormatException($"Некорректный e-mail: «{email}».");
        }

        return value;
    }
}
