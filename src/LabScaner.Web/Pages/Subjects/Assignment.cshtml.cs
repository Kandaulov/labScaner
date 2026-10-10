using LabScaner.Core.Teaching;
using LabScaner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Web.Pages.Subjects;

/// <summary>Задание одной работы: название, текст и чек-лист — то, что уйдёт в проверку ИИ.</summary>
public sealed class AssignmentModel(LabScanerDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public Assignment Work { get; private set; } = null!;

    public SubjectTerm SubjectTerm { get; private set; } = null!;

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await LoadAsync() ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(string? title, string? taskText, string? checklist, bool ai)
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }

        try
        {
            Work.Update(title, Work.DeadlineOverride, ai);
            Work.SetTask(taskText, (checklist ?? string.Empty).Split('\n'));
            await db.SaveChangesAsync();
            Message = "Сохранено.";
        }
        catch (ArgumentException ex)
        {
            Error = ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", "", StringComparison.Ordinal);
            await db.Entry(Work).ReloadAsync();
        }

        await LoadAsync();
        return Page();
    }

    private async Task<bool> LoadAsync()
    {
        var work = await db.Assignments.SingleOrDefaultAsync(a => a.Id == Id);
        if (work is null)
        {
            return false;
        }

        Work = work;
        SubjectTerm = await db.SubjectTerms
            .Include(s => s.Subject).Include(s => s.Term).Include(s => s.Assignments)
            .AsSplitQuery()
            .SingleAsync(s => s.Id == work.SubjectTermId);
        return true;
    }
}
