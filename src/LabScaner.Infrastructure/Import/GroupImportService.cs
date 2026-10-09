using System.Text.Json;
using LabScaner.Core.Abstractions;
using LabScaner.Core.Directory;
using LabScaner.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Infrastructure.Import;

public enum ImportRowStatus
{
    /// <summary>Новый студент.</summary>
    New,

    /// <summary>Уже есть в группе — ничего не изменится.</summary>
    Existing,

    /// <summary>Уже есть в группе — уточнится отчество или добавится e-mail.</summary>
    Updated,

    /// <summary>Строка не будет загружена.</summary>
    Error,
}

public sealed record ImportPreviewRow(int RowNumber, string Name, string? Topic, string? Email, ImportRowStatus Status, string? Note);

public sealed record ImportPreviewSheet(
    string SheetName,
    string? GroupName,
    bool GroupExists,
    string? Error,
    IReadOnlyList<ImportPreviewRow> Rows,
    IReadOnlyList<string> MissingInFile)
{
    public int Count(ImportRowStatus status) => Rows.Count(r => r.Status == status);

    public bool CanImport => Error is null && GroupName is not null && Rows.Any(r => r.Status is ImportRowStatus.New or ImportRowStatus.Updated);
}

public sealed record ImportPreview(string FileName, IReadOnlyList<ImportPreviewSheet> Sheets, string Payload);

public sealed record ImportResult(int GroupsCreated, int StudentsAdded, int StudentsUpdated, int EmailsAdded, IReadOnlyList<string> Groups);

/// <summary>
/// Импорт списков групп из XLSX (шаг 3.2): предпросмотр → подтверждение.
/// Разобранные строки между шагами хранятся в форме, подписанные Data Protection — повторно файл не нужен,
/// а подменить содержимое нельзя. Студентов из БД, которых нет в файле, импорт не трогает.
/// </summary>
public sealed class GroupImportService(LabScanerDbContext db, IDataProtectionProvider protection, IClock clock)
{
    private const string Purpose = "LabScaner.GroupImport.v1";

    public async Task<ImportPreview> PreviewAsync(Stream xlsx, string fileName, CancellationToken cancellationToken = default)
    {
        var sheets = XlsxReader.Read(xlsx).Select(s => GroupListParser.Parse(s.Name, s.Rows)).ToList();
        var groups = await db.Groups.Include(g => g.Students).ThenInclude(s => s.Emails)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var previews = new List<ImportPreviewSheet>();
        var payload = new List<PayloadSheet>();
        foreach (var sheet in sheets)
        {
            if (sheet.GroupName is null || sheet.Error is not null)
            {
                previews.Add(new ImportPreviewSheet(sheet.SheetName, sheet.GroupName, false, sheet.Error, [], []));
                continue;
            }

            var group = groups.FirstOrDefault(g => g.NameKey == GroupName.Key(sheet.GroupName));
            var others = groups.Where(g => g != group).ToList();
            var matched = new HashSet<Student>();
            var rows = new List<ImportPreviewRow>();
            foreach (var row in sheet.Rows)
            {
                if (row.Error is not null || row.Name is null)
                {
                    rows.Add(new ImportPreviewRow(row.RowNumber, row.Name?.FullName ?? row.RawName, row.Topic, row.Email, ImportRowStatus.Error, row.Error));
                    continue;
                }

                var existing = group?.Students.FirstOrDefault(s => StudentNames.Same(s.Name, row.Name));
                if (existing is not null)
                {
                    matched.Add(existing);
                    var notes = new List<string>();
                    if (existing.MiddleName is null && row.Name.MiddleName is not null)
                    {
                        notes.Add("добавится отчество");
                    }

                    if (row.Email is not null && existing.Emails.All(e => e.Email != row.Email))
                    {
                        notes.Add("добавится e-mail");
                    }

                    if (!existing.IsActive)
                    {
                        notes.Add("отчислен — статус не изменится");
                    }

                    rows.Add(new ImportPreviewRow(row.RowNumber, row.Name.FullName, row.Topic, row.Email,
                        notes.Any(n => n.StartsWith("добавится", StringComparison.Ordinal)) ? ImportRowStatus.Updated : ImportRowStatus.Existing,
                        notes.Count > 0 ? string.Join("; ", notes) : null));
                }
                else
                {
                    var elsewhere = others.FirstOrDefault(g => g.Students.Any(s => StudentNames.Same(s.Name, row.Name)));
                    rows.Add(new ImportPreviewRow(row.RowNumber, row.Name.FullName, row.Topic, row.Email, ImportRowStatus.New,
                        elsewhere is null ? null : $"такой студент есть в {elsewhere.Name} — проверьте, не перевод ли"));
                }
            }

            var missing = group?.Students.Where(s => s.IsActive && !matched.Contains(s)).Select(s => s.Name.FullName).Order(StringComparer.Ordinal).ToList() ?? [];
            previews.Add(new ImportPreviewSheet(sheet.SheetName, sheet.GroupName, group is not null, null, rows, missing));
            payload.Add(new PayloadSheet(
                sheet.GroupName,
                [.. sheet.ValidRows.Select(r => new PayloadRow(r.Name!.LastName, r.Name.FirstName, r.Name.MiddleName, r.Email))]));
        }

        var protectedPayload = protection.CreateProtector(Purpose).Protect(JsonSerializer.Serialize(payload));
        return new ImportPreview(fileName, previews, protectedPayload);
    }

    /// <exception cref="System.Security.Cryptography.CryptographicException">Данные формы подменены или устарели.</exception>
    public async Task<ImportResult> ApplyAsync(string protectedPayload, IReadOnlyCollection<string> groupNames, CancellationToken cancellationToken = default)
    {
        var json = protection.CreateProtector(Purpose).Unprotect(protectedPayload);
        var payload = JsonSerializer.Deserialize<List<PayloadSheet>>(json) ?? [];
        var selected = groupNames.Select(GroupName.Key).ToHashSet(StringComparer.Ordinal);
        var now = clock.UtcNow;

        int groupsCreated = 0, added = 0, updated = 0, emails = 0;
        var touched = new List<string>();
        foreach (var sheet in payload.Where(p => selected.Contains(GroupName.Key(p.GroupName))))
        {
            var key = GroupName.Key(sheet.GroupName);
            var group = await db.Groups.Include(g => g.Students).ThenInclude(s => s.Emails)
                .SingleOrDefaultAsync(g => g.NameKey == key, cancellationToken);
            if (group is null)
            {
                group = new Group(sheet.GroupName);
                db.Groups.Add(group);
                groupsCreated++;
            }

            touched.Add(group.Name);
            foreach (var row in sheet.Rows)
            {
                var name = new PersonName(row.LastName, row.FirstName, row.MiddleName);
                var student = group.Students.FirstOrDefault(s => StudentNames.Same(s.Name, name));
                if (student is null)
                {
                    student = group.AddStudent(name);
                    added++;
                }
                else if (student.RefineName(name))
                {
                    updated++;
                }

                if (row.Email is not null && student.Emails.All(e => e.Email != row.Email))
                {
                    student.AddEmail(row.Email, EmailSource.Import, now);
                    emails++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ImportResult(groupsCreated, added, updated, emails, touched);
    }

    private sealed record PayloadSheet(string GroupName, List<PayloadRow> Rows);

    private sealed record PayloadRow(string LastName, string FirstName, string? MiddleName, string? Email);
}
