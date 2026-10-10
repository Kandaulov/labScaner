using LabScaner.Core.Directory;
using LabScaner.Core.Teaching;
using LabScaner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Infrastructure.Import;

public sealed record TopicImportResult(int Set, int Unchanged, IReadOnlyList<string> NotFound, IReadOnlyList<string> SkippedSheets);

/// <summary>
/// Темы курсовых из списка группы (колонка «Тема КР», ADR-025): тот же файл, что для импорта групп.
/// Студенты ищутся только в группах этого предмета в семестре; пустая тема в файле ничего не стирает.
/// </summary>
public sealed class TopicImportService(LabScanerDbContext db)
{
    public async Task<TopicImportResult> ImportAsync(int subjectTermId, Stream xlsx, CancellationToken cancellationToken = default)
    {
        var subjectTerm = await db.SubjectTerms
            .Include(s => s.Groups).ThenInclude(g => g.Students)
            .Include(s => s.Topics)
            .SingleAsync(s => s.Id == subjectTermId, cancellationToken);

        int set = 0, unchanged = 0;
        var notFound = new List<string>();
        var skipped = new List<string>();
        foreach (var sheet in XlsxReader.Read(xlsx).Select(s => GroupListParser.Parse(s.Name, s.Rows)))
        {
            var group = sheet.GroupName is null ? null : subjectTerm.Groups.FirstOrDefault(g => g.NameKey == GroupName.Key(sheet.GroupName));
            if (group is null)
            {
                skipped.Add(sheet.SheetName);
                continue;
            }

            foreach (var row in sheet.ValidRows.Where(r => r.Topic is not null))
            {
                var student = group.Students.FirstOrDefault(s => StudentNames.Same(s.Name, row.Name!));
                if (student is null)
                {
                    notFound.Add($"{group.Name}: {row.Name!.FullName}");
                    continue;
                }

                if (subjectTerm.TopicOf(student.Id) == row.Topic!.Trim())
                {
                    unchanged++;
                    continue;
                }

                subjectTerm.SetTopic(student.Id, row.Topic);
                set++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new TopicImportResult(set, unchanged, notFound, skipped);
    }
}
