using LabScaner.Core.Abstractions;
using LabScaner.Core.Connections;
using LabScaner.Core.Directory;
using LabScaner.Core.Teaching;
using LabScaner.Infrastructure.Disk;
using LabScaner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Web.Pages.Settings;

/// <summary>Яндекс Диск преподавателя (ADR-011, ADR-031): подключение по коду, проверка и создание папок семестров.</summary>
public sealed class DiskModel(LabScanerDbContext db, DiskConnectionService service, IYandexOAuth oauth, IClock clock) : PageModel
{
    public DiskConnection? Connection { get; private set; }

    public bool Configured => oauth.IsConfigured;

    public Uri AuthorizeUrl => oauth.AuthorizeUrl;

    /// <summary>Предметы в семестрах, которые идут или ещё не начались.</summary>
    public IReadOnlyList<SubjectTerm> SubjectTerms { get; private set; } = [];

    public IReadOnlyList<FolderReport> Reports { get; private set; } = [];

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    [BindProperty(SupportsGet = true)]
    public bool Reconnect { get; set; }

    public DateOnly Today => DateOnly.FromDateTime(Navigation.AppTime.Local(clock.UtcNow).DateTime);

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostConnectAsync(string? code)
    {
        try
        {
            Connection = await service.ConnectAsync(code ?? string.Empty, HttpContext.RequestAborted);
            Message = $"Диск подключён: {Connection.DisplayName ?? Connection.Login}.";
            Reconnect = false;
            await LoadAsync();
            Reports = await service.CheckFoldersAsync(Connection, SubjectTerms, HttpContext.RequestAborted);
        }
        catch (DiskException ex)
        {
            Error = ex.Message;
            Reconnect = true;
            await LoadAsync();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCheckAsync()
    {
        await LoadAsync();
        if (Connection is null)
        {
            return RedirectToPage();
        }

        if (await service.CheckAsync(Connection, HttpContext.RequestAborted))
        {
            Reports = await TryAsync(() => service.CheckFoldersAsync(Connection, SubjectTerms, HttpContext.RequestAborted)) ?? [];
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(int st, bool root)
    {
        await LoadAsync();
        var subjectTerm = SubjectTerms.FirstOrDefault(s => s.Id == st);
        if (Connection is null || subjectTerm is null)
        {
            return NotFound();
        }

        var created = await TryAsync(() => service.CreateFoldersAsync(Connection, subjectTerm, root, HttpContext.RequestAborted));
        if (created is { Error: null })
        {
            Message = created.Complete ? $"Папки «{subjectTerm.Title}» готовы." : "Не все папки удалось создать — см. таблицу.";
        }
        else if (created?.Error is { } error)
        {
            Error = error;
        }

        Reports = await TryAsync(() => service.CheckFoldersAsync(Connection, SubjectTerms, HttpContext.RequestAborted)) ?? [];
        return Page();
    }

    public async Task<IActionResult> OnPostDisconnectAsync()
    {
        await service.DisconnectAsync(HttpContext.RequestAborted);
        Message = "Диск отключён: токены удалены. Файлы на Диске остаются как были.";
        await LoadAsync();
        return Page();
    }

    private async Task<T?> TryAsync<T>(Func<Task<T>> action)
        where T : class
    {
        try
        {
            return await action();
        }
        catch (DiskException ex)
        {
            Error = ex.Message;
            return null;
        }
    }

    private async Task LoadAsync()
    {
        Connection = await service.FindAsync(HttpContext.RequestAborted);
        var all = await db.SubjectTerms
            .Include(s => s.Subject).Include(s => s.Term).Include(s => s.Groups).Include(s => s.Assignments)
            .AsSplitQuery()
            .ToListAsync();
        SubjectTerms = [.. all
            .Where(s => s.Subject!.IsActive && s.Term!.StateOn(Today) != TermState.Past)
            .OrderBy(s => s.Term!.PeriodStart)
            .ThenBy(s => s.Subject!.Code, StringComparer.CurrentCulture)];
    }
}
