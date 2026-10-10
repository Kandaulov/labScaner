using LabScaner.Core.Abstractions;
using LabScaner.Core.Connections;
using LabScaner.Core.Directory;
using LabScaner.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Web.Pages.Settings;

/// <summary>Почтовый ящик преподавателя (ADR-016, ADR-024): настройки IMAP/SMTP и проверка без изменений в ящике.</summary>
public sealed class MailModel(LabScanerDbContext db, ISecretProtector secrets, IMailProbe probe, IClock clock) : PageModel
{
    public MailConnection? Connection { get; private set; }

    [BindProperty]
    public MailInput Input { get; set; } = new();

    public string? Message { get; private set; }

    public string? Error { get; private set; }

    public IReadOnlyList<MailProbeResult> Results { get; private set; } = [];

    /// <summary>Пароль сохранён, но не расшифровывается (ключи шифрования потеряны) — нужно ввести заново.</summary>
    public bool PasswordLost { get; private set; }

    public DateOnly Today => DateOnly.FromDateTime(AppTime.Local(clock.UtcNow).DateTime);

    public async Task OnGetAsync()
    {
        await LoadAsync();
        Input = Connection is null ? await DefaultsAsync() : MailInput.From(Connection);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoadAsync();
        var isNew = Connection is null;
        try
        {
            if (string.IsNullOrWhiteSpace(Input.ImapHost) && string.IsNullOrWhiteSpace(Input.SmtpHost) && MailPresets.Guess(Input.Address) is { } preset)
            {
                (Input.ImapHost, Input.ImapPort, Input.ImapSecurity) = (preset.Imap.Host, preset.Imap.Port, preset.Imap.Security);
                (Input.SmtpHost, Input.SmtpPort, Input.SmtpSecurity) = (preset.Smtp.Host, preset.Smtp.Port, preset.Smtp.Security);
            }

            var imap = new MailEndpoint(Input.ImapHost ?? string.Empty, Input.ImapPort, Input.ImapSecurity);
            var smtp = new MailEndpoint(Input.SmtpHost ?? string.Empty, Input.SmtpPort, Input.SmtpSecurity);
            var readSince = Input.ReadSince ?? Today;
            if (isNew && string.IsNullOrEmpty(Input.Password))
            {
                throw new ArgumentException("Введите пароль ящика (для Яндекса и Mail.ru — пароль приложения).");
            }

            if (Connection is null)
            {
                Connection = new MailConnection(Input.Address ?? string.Empty, Input.Login, imap, smtp, readSince);
                db.MailConnections.Add(Connection);
            }
            else
            {
                Connection.Update(Input.Address ?? string.Empty, Input.Login, imap, smtp, readSince);
            }

            if (!string.IsNullOrEmpty(Input.Password))
            {
                Connection.SetPassword(secrets.Protect(Input.Password));
            }

            await db.SaveChangesAsync();
            Message = isNew ? "Почта подключена." : "Настройки сохранены.";
            await CheckAsync(Input.Password);
        }
        catch (ArgumentException ex)
        {
            Error = ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", "", StringComparison.Ordinal);
            if (Connection is not null && !isNew)
            {
                await db.Entry(Connection).ReloadAsync();
            }
            else
            {
                Connection = null;
            }

            Input.Password = null;
            return Page();
        }

        await LoadAsync();
        Input = MailInput.From(Connection!);
        return Page();
    }

    public async Task<IActionResult> OnPostCheckAsync()
    {
        await LoadAsync();
        if (Connection is null)
        {
            return RedirectToPage();
        }

        await CheckAsync(null);
        Input = MailInput.From(Connection);
        return Page();
    }

    public async Task<IActionResult> OnPostSendTestAsync()
    {
        await LoadAsync();
        if (Connection is null)
        {
            return RedirectToPage();
        }

        Input = MailInput.From(Connection);
        var password = secrets.Unprotect(Connection.PasswordProtected);
        if (password is null)
        {
            Error = "Пароль не расшифровывается — введите его заново и сохраните.";
            return Page();
        }

        Results = [await probe.SendTestAsync(Connection.Smtp, Connection.Login, password, Connection.Address, HttpContext.RequestAborted)];
        return Page();
    }

    public async Task<IActionResult> OnPostDisconnectAsync()
    {
        await LoadAsync();
        if (Connection is not null)
        {
            db.MailConnections.Remove(Connection);
            await db.SaveChangesAsync();
        }

        await LoadAsync();
        Input = await DefaultsAsync();
        Message = "Почта отключена: пароль удалён, письма из ящика больше не читаются.";
        return Page();
    }

    /// <summary>IMAP и SMTP по очереди; итог записывается в подключение и виден в боковой панели.</summary>
    private async Task CheckAsync(string? typedPassword)
    {
        var password = string.IsNullOrEmpty(typedPassword) ? secrets.Unprotect(Connection!.PasswordProtected) : typedPassword;
        if (password is null)
        {
            PasswordLost = true;
            Error = "Сохранённый пароль не расшифровывается (сменились ключи шифрования на сервере) — введите пароль заново.";
            return;
        }

        var imap = await probe.ProbeImapAsync(Connection!.Imap, Connection.Login, password, HttpContext.RequestAborted);
        var smtp = await probe.ProbeSmtpAsync(Connection.Smtp, Connection.Login, password, HttpContext.RequestAborted);
        Results = [imap, smtp];
        Connection.RecordCheck(clock.UtcNow, imap.Ok && smtp.Ok, imap.Ok && smtp.Ok ? imap.Message : string.Join(" ", Results.Where(r => !r.Ok).Select(r => r.Message)));
        await db.SaveChangesAsync();
    }

    private async Task LoadAsync()
    {
        Connection = await db.MailConnections.SingleOrDefaultAsync();
        PasswordLost = Connection is { HasPassword: true } && secrets.Unprotect(Connection.PasswordProtected) is null;
    }

    /// <summary>Для нового ящика: порты с SSL и чтение с начала текущего семестра (ADR-024).</summary>
    private async Task<MailInput> DefaultsAsync()
    {
        var terms = await db.Terms.AsNoTracking().ToListAsync();
        var current = terms.FirstOrDefault(t => t.StateOn(Today) == TermState.Current);
        return new MailInput { ReadSince = current?.PeriodStart ?? Today.AddDays(-30) };
    }

    public sealed class MailInput
    {
        public string? Address { get; set; }

        public string? Login { get; set; }

        public string? Password { get; set; }

        public string? ImapHost { get; set; }

        public int ImapPort { get; set; } = 993;

        public MailSecurity ImapSecurity { get; set; } = MailSecurity.SslOnConnect;

        public string? SmtpHost { get; set; }

        public int SmtpPort { get; set; } = 465;

        public MailSecurity SmtpSecurity { get; set; } = MailSecurity.SslOnConnect;

        public DateOnly? ReadSince { get; set; }

        public static MailInput From(MailConnection c) => new()
        {
            Address = c.Address,
            Login = c.Login == c.Address ? null : c.Login,
            ImapHost = c.ImapHost,
            ImapPort = c.ImapPort,
            ImapSecurity = c.ImapSecurity,
            SmtpHost = c.SmtpHost,
            SmtpPort = c.SmtpPort,
            SmtpSecurity = c.SmtpSecurity,
            ReadSince = c.ReadSince,
        };
    }
}
