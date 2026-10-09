using LabScaner.Core.Abstractions;
using LabScaner.Core.Directory;
using LabScaner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Web.Pages.Settings;

public sealed class IndexModel(LabScanerDbContext db, IClock clock) : PageModel
{
    public Term? CurrentTerm { get; private set; }

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, Navigation.AppTime.Zone).DateTime);
        var terms = await db.Terms.AsNoTracking().ToListAsync();
        CurrentTerm = terms.FirstOrDefault(t => t.StateOn(today) == TermState.Current);
    }
}
