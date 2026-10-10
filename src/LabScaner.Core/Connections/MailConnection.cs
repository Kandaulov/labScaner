using System.Net.Mail;
using LabScaner.Core.Abstractions;

namespace LabScaner.Core.Connections;

public enum MailSecurity
{
    /// <summary>SSL/TLS сразу при подключении (IMAP 993, SMTP 465).</summary>
    SslOnConnect,

    /// <summary>Обычное подключение с переходом на TLS (IMAP 143, SMTP 587).</summary>
    StartTls,

    /// <summary>Без шифрования — только для проверки в локальной сети.</summary>
    None,
}

/// <summary>Адрес почтового сервера.</summary>
public sealed record MailEndpoint(string Host, int Port, MailSecurity Security)
{
    public static string? Validate(string? host, int port, string what)
    {
        var h = (host ?? string.Empty).Trim();
        if (h.Length == 0)
        {
            return $"Укажите сервер {what}.";
        }

        if (h.Length > 253 || h.Contains(' ', StringComparison.Ordinal) || h.Contains('/', StringComparison.Ordinal) || h.Contains(':', StringComparison.Ordinal))
        {
            return $"Сервер {what} — только имя, например imap.yandex.ru (без «https://» и порта).";
        }

        return port is < 1 or > 65535 ? $"Порт {what} — число от 1 до 65535." : null;
    }

    public override string ToString() => $"{Host}:{Port}";
}

/// <summary>
/// Почтовый ящик преподавателя (ADR-016, ADR-024): IMAP — приём работ (только чтение), SMTP — уведомления.
/// Пароль хранится зашифрованным (<see cref="ISecretProtector"/>). Один ящик на преподавателя.
/// </summary>
public sealed class MailConnection : ITeacherOwned
{
    public const int DefaultPollMinutes = 5;

    private MailConnection()
    {
        Address = string.Empty;
        Login = string.Empty;
        PasswordProtected = string.Empty;
        ImapHost = string.Empty;
        SmtpHost = string.Empty;
    }

    public MailConnection(string address, string login, MailEndpoint imap, MailEndpoint smtp, DateOnly readSince)
        : this()
    {
        Update(address, login, imap, smtp, readSince);
    }

    public int Id { get; private set; }

    public int TeacherId { get; private set; }

    /// <summary>Адрес ящика — с него уходят уведомления студентам.</summary>
    public string Address { get; private set; }

    public string Login { get; private set; }

    public string PasswordProtected { get; private set; }

    public string ImapHost { get; private set; }

    public int ImapPort { get; private set; }

    public MailSecurity ImapSecurity { get; private set; }

    public string SmtpHost { get; private set; }

    public int SmtpPort { get; private set; }

    public MailSecurity SmtpSecurity { get; private set; }

    /// <summary>Письма раньше этой даты не читаются (ADR-024: первый запуск — с начала семестра).</summary>
    public DateOnly ReadSince { get; private set; }

    /// <summary>Автоматический приём почты — включается на этапе 4.</summary>
    public bool PollEnabled { get; private set; }

    public int PollIntervalMinutes { get; private set; } = DefaultPollMinutes;

    /// <summary>Что уже прочитано (ADR-024): UIDVALIDITY «Входящих» и последний UID — заполняет приём почты.</summary>
    public long? ImapUidValidity { get; private set; }

    public long? ImapLastUid { get; private set; }

    public DateTimeOffset? LastCheckAt { get; private set; }

    public bool? LastCheckOk { get; private set; }

    public string? LastCheckMessage { get; private set; }

    public MailEndpoint Imap => new(ImapHost, ImapPort, ImapSecurity);

    public MailEndpoint Smtp => new(SmtpHost, SmtpPort, SmtpSecurity);

    public bool HasPassword => PasswordProtected.Length > 0;

    public void Update(string address, string? login, MailEndpoint imap, MailEndpoint smtp, DateOnly readSince)
    {
        ArgumentNullException.ThrowIfNull(imap);
        ArgumentNullException.ThrowIfNull(smtp);
        var cleanAddress = (address ?? string.Empty).Trim();
        if (!MailAddress.TryCreate(cleanAddress, out var parsed) || parsed.Address != cleanAddress || cleanAddress.Length > 254)
        {
            throw new ArgumentException("Укажите адрес ящика, например v.ivanov@ulstu.ru.", nameof(address));
        }

        var error = MailEndpoint.Validate(imap.Host, imap.Port, "IMAP") ?? MailEndpoint.Validate(smtp.Host, smtp.Port, "SMTP");
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(imap));
        }

        var cleanLogin = string.IsNullOrWhiteSpace(login) ? cleanAddress : login.Trim();
        if (cleanLogin.Length > 254)
        {
            throw new ArgumentException("Логин слишком длинный.", nameof(login));
        }

        var imapHost = imap.Host.Trim().ToLowerInvariant();
        var settingsChanged = Login != cleanLogin || ImapHost != imapHost || ImapPort != imap.Port;
        Address = cleanAddress;
        Login = cleanLogin;
        ImapHost = imapHost;
        ImapPort = imap.Port;
        ImapSecurity = imap.Security;
        SmtpHost = smtp.Host.Trim().ToLowerInvariant();
        SmtpPort = smtp.Port;
        SmtpSecurity = smtp.Security;
        ReadSince = readSince;

        // Другой ящик — прочитанное в старом не в счёт.
        if (settingsChanged)
        {
            ImapUidValidity = null;
            ImapLastUid = null;
        }
    }

    public void SetPassword(string passwordProtected)
    {
        ArgumentException.ThrowIfNullOrEmpty(passwordProtected);
        PasswordProtected = passwordProtected;
    }

    public void RecordCheck(DateTimeOffset at, bool ok, string message)
    {
        LastCheckAt = at;
        LastCheckOk = ok;
        LastCheckMessage = message.Length <= 500 ? message : message[..500];
    }

    public void AssignTeacher(int teacherId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(teacherId);
        if (TeacherId != 0 && TeacherId != teacherId)
        {
            throw new InvalidOperationException("Почтовый ящик принадлежит другому преподавателю.");
        }

        TeacherId = teacherId;
    }
}

/// <summary>Настройки популярных почтовых сервисов — подставляются по домену адреса, если сервер не указан.</summary>
public static class MailPresets
{
    private static readonly Dictionary<string, (string Imap, string Smtp)> _byDomain = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yandex.ru"] = ("imap.yandex.ru", "smtp.yandex.ru"),
        ["ya.ru"] = ("imap.yandex.ru", "smtp.yandex.ru"),
        ["yandex.com"] = ("imap.yandex.com", "smtp.yandex.com"),
        ["mail.ru"] = ("imap.mail.ru", "smtp.mail.ru"),
        ["bk.ru"] = ("imap.mail.ru", "smtp.mail.ru"),
        ["list.ru"] = ("imap.mail.ru", "smtp.mail.ru"),
        ["inbox.ru"] = ("imap.mail.ru", "smtp.mail.ru"),
        ["gmail.com"] = ("imap.gmail.com", "smtp.gmail.com"),
    };

    /// <summary>IMAP 993 и SMTP 465 с SSL для известного домена; иначе <c>null</c>.</summary>
    public static (MailEndpoint Imap, MailEndpoint Smtp)? Guess(string? address)
    {
        var at = (address ?? string.Empty).LastIndexOf('@');
        if (at < 0 || !_byDomain.TryGetValue(address![(at + 1)..].Trim(), out var hosts))
        {
            return null;
        }

        return (new MailEndpoint(hosts.Imap, 993, MailSecurity.SslOnConnect), new MailEndpoint(hosts.Smtp, 465, MailSecurity.SslOnConnect));
    }
}
