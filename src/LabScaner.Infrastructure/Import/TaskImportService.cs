using LabScaner.Core.Abstractions;
using LabScaner.Core.Teaching;
using LabScaner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Infrastructure.Import;

/// <summary>Краткие сведения о загруженном документе — без содержимого.</summary>
public sealed record TaskDocumentInfo(int Id, TaskDocumentKind Kind, string FileName, long Size, DateTimeOffset UploadedAt, DateTimeOffset? AppliedAt);

/// <summary>
/// Задания из DOCX (ADR-012, ADR-029): загрузка → предпросмотр деления → применение к работам.
/// Между шагами документ лежит в БД (неприменённым), поэтому предпросмотр можно открыть заново.
/// </summary>
public sealed class TaskImportService(LabScanerDbContext db, IClock clock)
{
    /// <summary>Сохраняет документ, предварительно проверив, что он читается. Прежние неприменённые загрузки того же вида удаляются.</summary>
    /// <exception cref="InvalidDataException">Файл не DOCX или слишком большой.</exception>
    public async Task<TaskDocument> UploadAsync(int subjectTermId, TaskDocumentKind kind, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
        {
            throw new InvalidDataException("Файл пуст.");
        }

        if (buffer.Length > TaskDocument.MaxSize)
        {
            throw new InvalidDataException("Файл больше 10 МБ — для заданий это слишком много. Проверьте, тот ли файл.");
        }

        buffer.Position = 0;
        var paragraphs = DocxReader.Read(buffer);
        if (paragraphs.All(p => string.IsNullOrWhiteSpace(p.Text)))
        {
            throw new InvalidDataException("В документе нет текста.");
        }

        if (!await db.SubjectTerms.AnyAsync(s => s.Id == subjectTermId, cancellationToken))
        {
            throw new InvalidOperationException("Предмет в семестре не найден.");
        }

        var stale = await db.TaskDocuments
            .Where(d => d.SubjectTermId == subjectTermId && d.Kind == kind && d.AppliedAt == null)
            .ToListAsync(cancellationToken);
        db.TaskDocuments.RemoveRange(stale);

        var document = new TaskDocument(subjectTermId, kind, fileName, buffer.ToArray(), clock.UtcNow);
        db.TaskDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        return document;
    }

    public Task<TaskDocument?> FindAsync(int subjectTermId, int documentId, TaskDocumentKind kind, CancellationToken cancellationToken = default) =>
        db.TaskDocuments.SingleOrDefaultAsync(d => d.Id == documentId && d.SubjectTermId == subjectTermId && d.Kind == kind, cancellationToken);

    public async Task<IReadOnlyList<TaskDocumentInfo>> ListAsync(int subjectTermId, CancellationToken cancellationToken = default) =>
        await db.TaskDocuments.AsNoTracking()
            .Where(d => d.SubjectTermId == subjectTermId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new TaskDocumentInfo(d.Id, d.Kind, d.FileName, d.Size, d.UploadedAt, d.AppliedAt))
            .ToListAsync(cancellationToken);

    public static TaskSplit Split(TaskDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var stream = new MemoryStream(document.Content, writable: false);
        return TaskDocumentSplitter.Split(DocxReader.Read(stream));
    }

    /// <summary>Применяет деление к лабораторным предмета в семестре.</summary>
    /// <exception cref="ArgumentException">В документе нет лаб, слишком много лаб или слишком длинный текст.</exception>
    public async Task<TaskSplit> ApplyLabsAsync(
        SubjectTerm subjectTerm,
        TaskDocument document,
        IReadOnlyDictionary<int, string>? titles,
        bool removeMissing,
        bool replaceRequirements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subjectTerm);
        ArgumentNullException.ThrowIfNull(document);
        var split = Split(document);
        subjectTerm.ApplyLabTasks(split.Labs, titles, removeMissing);
        if (replaceRequirements && split.Preamble.Length > 0)
        {
            subjectTerm.SetGeneralRequirements(split.Preamble);
        }

        document.MarkApplied(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return split;
    }

    /// <summary>Задание на курсовую применяется сразу: делить нечего.</summary>
    public async Task<CourseworkTask> ApplyCourseworkAsync(SubjectTerm subjectTerm, TaskDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subjectTerm);
        ArgumentNullException.ThrowIfNull(document);
        using var stream = new MemoryStream(document.Content, writable: false);
        var task = TaskDocumentSplitter.ParseCoursework(DocxReader.Read(stream));
        subjectTerm.ApplyCourseworkTask(task);
        document.MarkApplied(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return task;
    }
}
