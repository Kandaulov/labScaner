using LabScaner.Core.Teaching;
using LabScaner.Infrastructure.Import;
using LabScaner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Web.Pages.Subjects;

/// <summary>
/// Предпросмотр деления DOCX с заданиями на лабораторные (ADR-012, ADR-029): номер → название → начало текста
/// → черновик чек-листа. Преподаватель правит названия и подтверждает.
/// </summary>
public sealed class TasksModel(LabScanerDbContext db, TaskImportService tasks) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Doc { get; set; }

    public SubjectTerm SubjectTerm { get; private set; } = null!;

    public TaskDocument Document { get; private set; } = null!;

    public TaskSplit Split { get; private set; } = null!;

    public string? Error { get; private set; }

    /// <summary>Лабораторные, которых нет в документе и которые будут удалены при подтверждении.</summary>
    public IReadOnlyList<Assignment> Extra { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostApplyAsync(Dictionary<int, string>? titles, bool removeMissing, bool replaceRequirements)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        try
        {
            var split = await tasks.ApplyLabsAsync(SubjectTerm, Document, titles, removeMissing, replaceRequirements);
            TempData["Flash"] = $"Задания применены: лабораторных — {split.Labs.Count}, пунктов чек-листов — {split.Labs.Sum(l => l.Checklist.Count)}.";
            return RedirectToPage("/Subjects/Term", new { id = SubjectTerm.Id });
        }
        catch (ArgumentException ex)
        {
            Error = ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", "", StringComparison.Ordinal);
            await db.Entry(SubjectTerm).ReloadAsync();
            await LoadAsync();
            return Page();
        }
    }

    public static string Excerpt(string text, int length = 220)
    {
        var flat = text.ReplaceLineEndings(" ");
        return flat.Length <= length ? flat : flat[..length].TrimEnd() + "…";
    }

    private async Task<bool> LoadAsync()
    {
        var subjectTerm = await db.SubjectTerms
            .Include(s => s.Subject).Include(s => s.Term).Include(s => s.Assignments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == Id);
        var document = subjectTerm is null ? null : await tasks.FindAsync(subjectTerm.Id, Doc, TaskDocumentKind.Labs);
        if (subjectTerm is null || document is null)
        {
            return false;
        }

        SubjectTerm = subjectTerm;
        Document = document;
        try
        {
            Split = TaskImportService.Split(document);
        }
        catch (InvalidDataException ex)
        {
            Split = new TaskSplit(string.Empty, [], [ex.Message]);
        }

        var max = Split.Labs.Count == 0 ? int.MaxValue : Split.Labs.Max(l => l.Number);
        Extra = [.. subjectTerm.Labs.Where(l => l.Number > max)];
        return true;
    }
}
