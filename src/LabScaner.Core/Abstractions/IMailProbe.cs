using LabScaner.Core.Connections;

namespace LabScaner.Core.Abstractions;

/// <summary>Результат проверки подключения: понятный преподавателю текст, а при успехе — сведения о ящике.</summary>
public sealed record MailProbeResult(bool Ok, string Message, int? InboxCount = null, long? UidValidity = null);

/// <summary>
/// Проверка почтового ящика без изменений в нём (ADR-024): IMAP открывает «Входящие» только на чтение,
/// SMTP — вход без отправки. Пробное письмо уходит только на адрес самого ящика.
/// </summary>
public interface IMailProbe
{
    Task<MailProbeResult> ProbeImapAsync(MailEndpoint endpoint, string login, string password, CancellationToken cancellationToken = default);

    Task<MailProbeResult> ProbeSmtpAsync(MailEndpoint endpoint, string login, string password, CancellationToken cancellationToken = default);

    Task<MailProbeResult> SendTestAsync(MailEndpoint endpoint, string login, string password, string address, CancellationToken cancellationToken = default);
}
