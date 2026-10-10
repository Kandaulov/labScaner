using System.Security.Cryptography;
using LabScaner.Core.Abstractions;

namespace LabScaner.Core.Teaching;

public enum TaskDocumentKind
{
    /// <summary>Задания на все лабораторные.</summary>
    Labs,

    /// <summary>Задание на курсовую.</summary>
    Coursework,
}

/// <summary>
/// Загруженный DOCX с заданиями (ADR-012, ADR-029). Хранится в БД, пока не подключён Яндекс Диск;
/// потом копируется в «{корень}/Задания/». Применённый документ — тот, из которого взяты задания.
/// </summary>
public sealed class TaskDocument : ITeacherOwned
{
    public const long MaxSize = 10 * 1024 * 1024;

    private TaskDocument()
    {
        FileName = string.Empty;
        Sha256 = string.Empty;
        Content = [];
    }

    public TaskDocument(int subjectTermId, TaskDocumentKind kind, string fileName, byte[] content, DateTimeOffset uploadedAt)
        : this()
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0 || content.Length > MaxSize)
        {
            throw new ArgumentException("Файл пуст или больше 10 МБ.", nameof(content));
        }

        SubjectTermId = subjectTermId;
        Kind = kind;
        FileName = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim() is { Length: > 0 } name
            ? (name.Length <= 255 ? name : name[^255..])
            : "задания.docx";
        Content = content;
        Size = content.Length;
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        UploadedAt = uploadedAt;
    }

    public int Id { get; private set; }

    public int TeacherId { get; private set; }

    public int SubjectTermId { get; private set; }

    public TaskDocumentKind Kind { get; private set; }

    public string FileName { get; private set; }

    public long Size { get; private set; }

    public string Sha256 { get; private set; }

    public byte[] Content { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    /// <summary>Когда задания из документа применены к работам; <c>null</c> — загружен, но не применён.</summary>
    public DateTimeOffset? AppliedAt { get; private set; }

    public void MarkApplied(DateTimeOffset now) => AppliedAt = now;

    public void AssignTeacher(int teacherId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(teacherId);
        if (TeacherId != 0 && TeacherId != teacherId)
        {
            throw new InvalidOperationException("Документ принадлежит другому преподавателю.");
        }

        TeacherId = teacherId;
    }
}
