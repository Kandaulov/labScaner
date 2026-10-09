using LabScaner.Core.Subjects;
using LabScaner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Web.Pages.Subjects;

/// <summary>Предметы преподавателя (ADR-010…012, ADR-016): видны и изменяются только свои.</summary>
public sealed class IndexModel(LabScanerDbContext db) : PageModel
{
    public IReadOnlyList<Subject> Subjects { get; private set; } = [];

    public Subject? Current { get; private set; }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool New { get; set; }

    [BindProperty]
    public SubjectInput Input { get; set; } = new();

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Пример значений для предпросмотра пути.</summary>
    public static DiskPathValues Example(string code) =>
        new(string.IsNullOrWhiteSpace(code) ? "КорпИС" : code.Trim(), "2026-2027", "ИСТ", 7);

    public async Task OnGetAsync()
    {
        await LoadAsync();
        if (Current is not null)
        {
            Input = SubjectInput.From(Current);
        }
        else
        {
            Input = new SubjectInput();
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var all = await db.Subjects.ToListAsync();
        var subject = Id is null ? null : all.SingleOrDefault(s => s.Id == Id);
        if (Id is not null && subject is null)
        {
            return NotFound();
        }

        var isNew = subject is null;
        try
        {
            var aliases = (Input.Aliases ?? string.Empty).Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            subject ??= new Subject(Input.Name ?? string.Empty, Input.Code ?? string.Empty);
            subject.Update(Input.Name ?? string.Empty, Input.Code ?? string.Empty, aliases, Input.FinalAssessment, Input.AiReferenceText, Input.DiskPathTemplate ?? string.Empty);

            var conflict = subject.ConflictWith(all);
            if (conflict is not null)
            {
                throw new ArgumentException(conflict);
            }

            if (isNew)
            {
                db.Subjects.Add(subject);
            }

            await db.SaveChangesAsync();
            Message = isNew ? $"Предмет «{subject.Code}» добавлен." : "Сохранено.";
            Id = subject.Id;
            New = false;
            ModelState.Clear();
            Input = SubjectInput.From(subject);
        }
        catch (ArgumentException ex)
        {
            Error = Clean(ex);
            if (!isNew)
            {
                await db.Entry(subject!).ReloadAsync();
            }
            else
            {
                New = true;
            }
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostArchiveAsync(bool restore)
    {
        var subject = await db.Subjects.SingleOrDefaultAsync(s => s.Id == Id);
        if (subject is null)
        {
            return NotFound();
        }

        if (restore)
        {
            subject.Restore();
            Message = $"«{subject.Code}» снова в списке читаемых предметов.";
        }
        else
        {
            subject.Archive();
            Message = $"«{subject.Code}» перенесён в архив: письма по нему больше не распознаются, данные сохранены.";
        }

        await db.SaveChangesAsync();
        await LoadAsync();
        Input = SubjectInput.From(subject);
        return Page();
    }

    /// <summary>Предпросмотр пути на Диске по шаблону (htmx, при вводе).</summary>
    public PartialViewResult OnPostPathPreview() =>
        Partial("_PathPreview", new PathPreview(Input.DiskPathTemplate ?? string.Empty, Input.Code ?? string.Empty));

    private async Task LoadAsync()
    {
        var subjects = await db.Subjects.AsNoTracking().ToListAsync();
        Subjects = [.. subjects.OrderByDescending(s => s.IsActive).ThenBy(s => s.Code, StringComparer.CurrentCulture)];
        Current = New ? null : Subjects.FirstOrDefault(s => s.Id == Id) ?? (Id is null ? Subjects.FirstOrDefault() : null);
        Id = Current?.Id;
        if (Subjects.Count == 0)
        {
            New = true;
        }
    }

    private static string Clean(ArgumentException ex) =>
        ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", "", StringComparison.Ordinal);

    public sealed class SubjectInput
    {
        public string? Name { get; set; }

        public string? Code { get; set; }

        /// <summary>Через запятую.</summary>
        public string? Aliases { get; set; }

        public FinalAssessment FinalAssessment { get; set; } = FinalAssessment.Exam;

        public string? AiReferenceText { get; set; }

        public string? DiskPathTemplate { get; set; } = Core.Subjects.DiskPathTemplate.Default;

        public static SubjectInput From(Subject s) => new()
        {
            Name = s.Name,
            Code = s.Code,
            Aliases = string.Join(", ", s.Aliases),
            FinalAssessment = s.FinalAssessment,
            AiReferenceText = s.AiReferenceText,
            DiskPathTemplate = s.DiskPathTemplate,
        };
    }
}

public sealed record PathPreview(string Template, string Code)
{
    public string? Error => DiskPathTemplate.Validate(Template);

    public IReadOnlyList<string> Segments =>
        Error is null ? DiskPathTemplate.Render(Template, IndexModel.Example(Code)).Split('/') : [];
}
