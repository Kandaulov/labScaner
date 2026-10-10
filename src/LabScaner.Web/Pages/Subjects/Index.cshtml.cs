using LabScaner.Core.Abstractions;
using LabScaner.Core.Directory;
using LabScaner.Core.Subjects;
using LabScaner.Core.Teaching;
using LabScaner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Web.Pages.Subjects;

/// <summary>Предметы преподавателя (ADR-010…012, ADR-016): видны и изменяются только свои.</summary>
public sealed class IndexModel(LabScanerDbContext db, IClock clock) : PageModel
{
    public IReadOnlyList<Subject> Subjects { get; private set; } = [];

    /// <summary>Семестры текущего предмета (ADR-011): новые сверху.</summary>
    public IReadOnlyList<SubjectTerm> SubjectTerms { get; private set; } = [];

    /// <summary>Семестры календаря, в которых предмета ещё нет.</summary>
    public IReadOnlyList<Term> FreeTerms { get; private set; } = [];

    public IReadOnlyList<Group> AllGroups { get; private set; } = [];

    [BindProperty]
    public TermInput NewTerm { get; set; } = new();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, Navigation.AppTime.Zone).DateTime);

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

    /// <summary>Предмет в семестре: группы, работы, путь на Диске по шаблону — дальше правится на своей странице.</summary>
    public async Task<IActionResult> OnPostAddTermAsync()
    {
        var subject = await db.Subjects.SingleOrDefaultAsync(s => s.Id == Id);
        if (subject is null)
        {
            return NotFound();
        }

        var term = await db.Terms.SingleOrDefaultAsync(t => t.Id == NewTerm.TermId);
        var groupIds = (NewTerm.GroupIds ?? []).ToHashSet();
        var groups = (await db.Groups.Where(g => groupIds.Contains(g.Id)).ToListAsync()).OrderBy(g => g.Name, StringComparer.Ordinal).ToList();
        try
        {
            if (term is null)
            {
                throw new ArgumentException("Выберите семестр из календаря.");
            }

            if (groups.Count == 0)
            {
                throw new ArgumentException("Выберите хотя бы одну группу.");
            }

            if (await db.SubjectTerms.AnyAsync(s => s.SubjectId == subject.Id && s.TermId == term.Id))
            {
                throw new ArgumentException($"«{subject.Code}» в семестре {term.Title} уже заведён.");
            }

            var path = SubjectTerm.SuggestPath(subject, term, NewTerm.StudySemester, groups[0].Direction);
            var subjectTerm = new SubjectTerm(subject, term, NewTerm.StudySemester, path);
            groups.ForEach(subjectTerm.AddGroup);
            subjectTerm.EnsureLabs(NewTerm.Labs);
            subjectTerm.SetCoursework(NewTerm.Coursework);
            db.SubjectTerms.Add(subjectTerm);
            await db.SaveChangesAsync();
            return RedirectToPage("/Subjects/Term", new { id = subjectTerm.Id });
        }
        catch (ArgumentException ex)
        {
            Error = Clean(ex);
        }

        await LoadAsync();
        if (Current is not null)
        {
            Input = SubjectInput.From(Current);
        }

        return Page();
    }

    /// <summary>Предпросмотр пути на Диске по шаблону (htmx, при вводе).</summary>
    public PartialViewResult OnPostPathPreview() =>
        Partial("_PathPreview", new PathPreview(Input.DiskPathTemplate ?? string.Empty, Input.Code ?? string.Empty));

    private async Task LoadAsync()
    {
        var subjects = await db.Subjects.AsNoTracking().ToListAsync();
        Subjects = [.. subjects.OrderByDescending(s => s.IsActive).ThenBy(s => s.Code, StringComparer.CurrentCulture)];
        Current = New ? null : Subjects.FirstOrDefault(s => s.Id == Id) ?? (Id is null && Subjects.Count > 0 ? Subjects[0] : null);
        Id = Current?.Id;
        if (Subjects.Count == 0)
        {
            New = true;
        }

        if (Current is null)
        {
            return;
        }

        var subjectTerms = await db.SubjectTerms.AsNoTracking()
            .Include(s => s.Term).Include(s => s.Groups).Include(s => s.Assignments)
            .Where(s => s.SubjectId == Current.Id)
            .AsSplitQuery()
            .ToListAsync();
        SubjectTerms = [.. subjectTerms.OrderByDescending(s => s.Term!.PeriodStart)];
        var used = subjectTerms.Select(s => s.TermId).ToHashSet();
        var terms = await db.Terms.AsNoTracking().ToListAsync();
        FreeTerms = [.. terms.Where(t => !used.Contains(t.Id) && t.StateOn(Today) != TermState.Past).OrderBy(t => t.PeriodStart)];
        AllGroups = [.. (await db.Groups.AsNoTracking().ToListAsync()).OrderBy(g => g.Name, StringComparer.Ordinal)];

        // Предложение по прошлому семестру предмета: столько же лаб, курсовая — если была.
        var last = SubjectTerms.Count > 0 ? SubjectTerms[0] : null;
        if (last is not null && NewTerm.TermId == 0)
        {
            NewTerm = new TermInput
            {
                StudySemester = last.StudySemester,
                Labs = last.Assignments.Count(a => a.Kind == AssignmentKind.Lab),
                Coursework = last.Assignments.Any(a => a.Kind == AssignmentKind.Coursework),
            };
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

public sealed class TermInput
{
    public int TermId { get; set; }

    public int StudySemester { get; set; } = 7;

    public int[]? GroupIds { get; set; }

    public int Labs { get; set; } = 8;

    public bool Coursework { get; set; }
}

public sealed record PathPreview(string Template, string Code)
{
    public string? Error => DiskPathTemplate.Validate(Template);

    public IReadOnlyList<string> Segments =>
        Error is null ? DiskPathTemplate.Render(Template, IndexModel.Example(Code)).Split('/') : [];
}
