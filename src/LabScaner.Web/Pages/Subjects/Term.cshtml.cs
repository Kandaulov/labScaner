using LabScaner.Core.Directory;
using LabScaner.Core.Teaching;
using LabScaner.Infrastructure.Import;
using LabScaner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Web.Pages.Subjects;

/// <summary>Предмет в семестре: группы, папка на Диске, работы и дедлайны, темы курсовых.</summary>
[RequestSizeLimit(DocxReader.MaxFileSize + (64 * 1024))]
public sealed class TermModel(LabScanerDbContext db, TopicImportService topics, TaskImportService tasks) : PageModel
{
    public SubjectTerm SubjectTerm { get; private set; } = null!;

    public IReadOnlyList<Group> AllGroups { get; private set; } = [];

    public string SuggestedPath { get; private set; } = string.Empty;

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public TopicImportResult? TopicResult { get; private set; }

    /// <summary>Загруженные документы с заданиями (без содержимого).</summary>
    public IReadOnlyList<TaskDocumentInfo> Documents { get; private set; } = [];

    /// <summary>Другие семестры этого предмета, из которых можно скопировать работы (ADR-012).</summary>
    public IReadOnlyList<SubjectTerm> CopySources { get; private set; } = [];

    /// <summary>Предлагаемый источник копирования: тот же семестр обучения, самый свежий.</summary>
    public int? SuggestedSourceId { get; private set; }

    public CourseworkTask? CourseworkResult { get; private set; }

    [TempData]
    public string? Flash { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Message = Flash;
        return await LoadAsync() ? Page() : NotFound();
    }

    /// <summary>DOCX с заданиями на лабораторные → предпросмотр деления на отдельной странице.</summary>
    public async Task<IActionResult> OnPostUploadLabsAsync(IFormFile? file)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        var error = CheckDocx(file);
        if (error is null)
        {
            try
            {
                await using var stream = file!.OpenReadStream();
                var document = await tasks.UploadAsync(SubjectTerm.Id, TaskDocumentKind.Labs, file.FileName, stream);
                return RedirectToPage("/Subjects/Tasks", new { id = SubjectTerm.Id, doc = document.Id });
            }
            catch (InvalidDataException ex)
            {
                error = ex.Message;
            }
        }

        Error = error;
        return Page();
    }

    /// <summary>DOCX с заданием на курсовую применяется сразу.</summary>
    public async Task<IActionResult> OnPostUploadCourseworkAsync(IFormFile? file)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        Error = CheckDocx(file);
        if (Error is null)
        {
            try
            {
                await using var stream = file!.OpenReadStream();
                var document = await tasks.UploadAsync(SubjectTerm.Id, TaskDocumentKind.Coursework, file.FileName, stream);
                CourseworkResult = await tasks.ApplyCourseworkAsync(SubjectTerm, document);
                Message = $"Задание на курсовую загружено: {CourseworkResult.Text.Length} знаков, пунктов чек-листа — {CourseworkResult.Checklist.Count}.";
            }
            catch (InvalidDataException ex)
            {
                Error = ex.Message;
            }
            catch (ArgumentException ex)
            {
                Error = Clean(ex);
            }
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRequirementsAsync(string? generalRequirements)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        try
        {
            SubjectTerm.SetGeneralRequirements(generalRequirements);
            await db.SaveChangesAsync();
            Message = "Общие требования сохранены.";
        }
        catch (ArgumentException ex)
        {
            Error = Clean(ex);
            await db.Entry(SubjectTerm).ReloadAsync();
        }

        await LoadAsync();
        return Page();
    }

    /// <summary>Копирует работы, задания, чек-листы и общие требования из другого семестра предмета.</summary>
    public async Task<IActionResult> OnPostCopyAsync(int sourceId)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        var source = CopySources.FirstOrDefault(s => s.Id == sourceId);
        if (source is null)
        {
            Error = "Выберите семестр, из которого копировать.";
            return Page();
        }

        SubjectTerm.CopyWorksFrom(source);
        await db.SaveChangesAsync();
        Message = $"Скопировано из {source.Term!.Title} · {source.StudySemester} сем: лаб — {SubjectTerm.Labs.Count()}{(SubjectTerm.Coursework is null ? "" : " + курсовая")}.";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostMainAsync(int studySemester, string? diskRootPath, bool useSuggested)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        try
        {
            SubjectTerm.SetStudySemester(studySemester);
            SubjectTerm.SetDiskRootPath(useSuggested ? Suggest(SubjectTerm) : diskRootPath ?? string.Empty);
            await db.SaveChangesAsync();
            Message = "Сохранено.";
        }
        catch (ArgumentException ex)
        {
            Error = Clean(ex);
            await db.Entry(SubjectTerm).ReloadAsync();
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostGroupsAsync(int[]? groupIds)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        var wanted = (groupIds ?? []).ToHashSet();
        foreach (var g in SubjectTerm.Groups.Where(g => !wanted.Contains(g.Id)).ToList())
        {
            SubjectTerm.RemoveGroup(g);
        }

        foreach (var g in AllGroups.Where(g => wanted.Contains(g.Id)))
        {
            SubjectTerm.AddGroup(g);
        }

        await db.SaveChangesAsync();
        Message = $"Группы: {(SubjectTerm.Groups.Count == 0 ? "не выбраны" : string.Join(", ", SubjectTerm.Groups.Select(g => g.Name).Order(StringComparer.Ordinal)))}.";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostWorksAsync(List<WorkInput>? works)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        try
        {
            foreach (var input in works ?? [])
            {
                var work = SubjectTerm.Assignments.FirstOrDefault(a => a.Id == input.Id);
                work?.Update(input.Title, input.Deadline, input.Ai);
            }

            await db.SaveChangesAsync();
            Message = "Работы сохранены.";
        }
        catch (ArgumentException ex)
        {
            Error = Clean(ex);
            foreach (var work in SubjectTerm.Assignments)
            {
                await db.Entry(work).ReloadAsync();
            }
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAddLabAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        try
        {
            var lab = SubjectTerm.AddLab();
            await db.SaveChangesAsync();
            Message = $"Добавлена {lab.ShortName}.";
        }
        catch (InvalidOperationException ex)
        {
            Error = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveLabAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        var last = SubjectTerm.Labs.LastOrDefault();
        SubjectTerm.RemoveLastLab();
        await db.SaveChangesAsync();
        Message = last is null ? "Лабораторных нет." : $"{last.ShortName} удалена.";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCourseworkAsync(bool enabled)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        SubjectTerm.SetCoursework(enabled);
        await db.SaveChangesAsync();
        Message = enabled ? "Курсовая добавлена." : "Курсовая убрана.";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostTopicsAsync(IFormFile? file)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        if (file is null || file.Length == 0 || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            Error = "Выберите список группы в формате .xlsx — тот же файл, что для импорта групп.";
            return Page();
        }

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;
            TopicResult = await topics.ImportAsync(SubjectTerm.Id, buffer);
        }
        catch (InvalidDataException ex)
        {
            Error = ex.Message;
        }

        await LoadAsync();
        return Page();
    }

    private async Task<bool> LoadAsync()
    {
        var subjectTerm = await db.SubjectTerms
            .Include(s => s.Subject)
            .Include(s => s.Term)
            .Include(s => s.Groups).ThenInclude(g => g.Students)
            .Include(s => s.Assignments)
            .Include(s => s.Topics)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == Id);
        if (subjectTerm is null)
        {
            return false;
        }

        SubjectTerm = subjectTerm;
        AllGroups = [.. (await db.Groups.Include(g => g.Students).ToListAsync()).OrderBy(g => g.Name, StringComparer.Ordinal)];
        SuggestedPath = Suggest(subjectTerm);
        Documents = await tasks.ListAsync(subjectTerm.Id);

        var sources = await db.SubjectTerms
            .Include(s => s.Term).Include(s => s.Assignments)
            .Where(s => s.SubjectId == subjectTerm.SubjectId && s.Id != subjectTerm.Id)
            .AsSplitQuery()
            .ToListAsync();
        CopySources = [.. sources.Where(s => s.Assignments.Count > 0).OrderByDescending(s => s.Term!.PeriodStart)];
        SuggestedSourceId = (CopySources.FirstOrDefault(s => s.StudySemester == subjectTerm.StudySemester && s.Term!.PeriodStart < subjectTerm.Term!.PeriodStart)
            ?? CopySources.FirstOrDefault(s => s.StudySemester == subjectTerm.StudySemester))?.Id;
        return true;
    }

    /// <summary>Последний применённый документ данного вида.</summary>
    public TaskDocumentInfo? Applied(TaskDocumentKind kind) =>
        Documents.Where(d => d.Kind == kind && d.AppliedAt is not null).MaxBy(d => d.AppliedAt);

    private static string? CheckDocx(IFormFile? file) =>
        file is null || file.Length == 0 ? "Выберите файл .docx."
        : !file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? "Нужен документ Word в формате .docx. Файл .doc откройте в Word и сохраните как .docx."
        : file.Length > DocxReader.MaxFileSize ? "Файл больше 10 МБ — для заданий это слишком много. Проверьте, тот ли файл."
        : null;

    /// <summary>Путь по шаблону предмета; направление — по первой группе (по алфавиту).</summary>
    private static string Suggest(SubjectTerm st)
    {
        var direction = st.Groups.OrderBy(g => g.Name, StringComparer.Ordinal).Select(g => g.Direction).FirstOrDefault() ?? "ИСТ";
        return SubjectTerm.SuggestPath(st.Subject!, st.Term!, st.StudySemester, direction);
    }

    private static string Clean(ArgumentException ex) =>
        ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", "", StringComparison.Ordinal);

    public sealed class WorkInput
    {
        public int Id { get; set; }

        public string? Title { get; set; }

        public DateOnly? Deadline { get; set; }

        public bool Ai { get; set; }
    }
}
