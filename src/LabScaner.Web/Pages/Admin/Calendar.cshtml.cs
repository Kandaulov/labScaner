using LabScaner.Core.Abstractions;
using LabScaner.Core.Directory;
using LabScaner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Web.Pages.Admin;

/// <summary>Учебный календарь (ADR-013): от дат считаются дедлайны лабораторных и курсовой.</summary>
public sealed class CalendarModel(LabScanerDbContext db, IClock clock) : PageModel
{
    public IReadOnlyList<Term> Terms { get; private set; } = [];

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, Navigation.AppTime.Zone).DateTime);

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public int? ErrorTermId { get; private set; }

    [BindProperty]
    public NewTermInput NewTerm { get; set; } = new();

    public async Task OnGetAsync()
    {
        NewTerm = Suggest(await LoadAsync());
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        try
        {
            if (NewTerm.CreditWeekStart is not { } cw || NewTerm.SessionStart is not { } ss)
            {
                throw new ArgumentException("Укажите обе даты.");
            }

            var term = new Term(NewTerm.StartYear, NewTerm.Season, cw, ss);
            if (await db.Terms.AnyAsync(t => t.AcademicYear == term.AcademicYear && t.Season == term.Season))
            {
                throw new ArgumentException($"Семестр «{term.Title}» уже есть.");
            }

            db.Terms.Add(term);
            await db.SaveChangesAsync();
            Message = $"Добавлен семестр «{term.Title}».";
            ModelState.Clear();
            NewTerm = Suggest(await LoadAsync());
        }
        catch (ArgumentException ex)
        {
            Error = Clean(ex);
            await LoadAsync();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int id, DateOnly? creditWeekStart, DateOnly? sessionStart)
    {
        var term = await db.Terms.SingleOrDefaultAsync(t => t.Id == id);
        if (term is null)
        {
            return NotFound();
        }

        try
        {
            if (creditWeekStart is not { } cw || sessionStart is not { } ss)
            {
                throw new ArgumentException("Укажите обе даты.");
            }

            term.SetCalendar(cw, ss);
            await db.SaveChangesAsync();
            Message = $"Сохранено: «{term.Title}».";
        }
        catch (ArgumentException ex)
        {
            Error = Clean(ex);
            ErrorTermId = id;
            db.Entry(term).State = EntityState.Unchanged;
        }

        await LoadAsync();
        return Page();
    }

    private async Task<IReadOnlyList<Term>> LoadAsync()
    {
        var terms = await db.Terms.AsNoTracking().ToListAsync();
        Terms = [.. terms.OrderByDescending(t => t.PeriodStart)];
        return Terms;
    }

    /// <summary>Предложить следующий незаведённый семестр: ближайший к сегодняшнему дню.</summary>
    private NewTermInput Suggest(IReadOnlyList<Term> existing)
    {
        var year = Today.Month >= 9 ? Today.Year : Today.Year - 1;
        var season = Today.Month is >= 2 and <= 8 ? TermSeason.Spring : TermSeason.Autumn;
        for (var i = 0; i < 6; i++)
        {
            if (!existing.Any(t => t.StartYear == year && t.Season == season))
            {
                break;
            }

            (year, season) = season == TermSeason.Autumn ? (year, TermSeason.Spring) : (year + 1, TermSeason.Autumn);
        }

        return new NewTermInput { StartYear = year, Season = season };
    }

    private static string Clean(ArgumentException ex) =>
        ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", "", StringComparison.Ordinal);

    public sealed class NewTermInput
    {
        public int StartYear { get; set; }

        public TermSeason Season { get; set; }

        public DateOnly? CreditWeekStart { get; set; }

        public DateOnly? SessionStart { get; set; }
    }
}
