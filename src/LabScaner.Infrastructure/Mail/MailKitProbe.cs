using System.Net.Sockets;
using LabScaner.Core.Abstractions;
using LabScaner.Core.Connections;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LabScaner.Infrastructure.Mail;

/// <summary>
/// Проверка ящика через MailKit. «Входящие» открываются только на чтение — флаги, папки и письма не меняются (ADR-024).
/// </summary>
public sealed class MailKitProbe : IMailProbe
{
    private const int TimeoutMs = 20_000;

    public async Task<MailProbeResult> ProbeImapAsync(MailEndpoint endpoint, string login, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        using var client = new ImapClient { Timeout = TimeoutMs };
        try
        {
            await client.ConnectAsync(endpoint.Host, endpoint.Port, Options(endpoint.Security), cancellationToken);
            await client.AuthenticateAsync(login, password, cancellationToken);
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
            var result = new MailProbeResult(true, $"IMAP: вход выполнен, во «Входящих» {inbox.Count} писем (открыто только на чтение).", inbox.Count, inbox.UidValidity);
            await client.DisconnectAsync(true, cancellationToken);
            return result;
        }
        catch (Exception ex) when (Explain(ex, "IMAP", endpoint) is { } message)
        {
            return new MailProbeResult(false, message);
        }
    }

    public async Task<MailProbeResult> ProbeSmtpAsync(MailEndpoint endpoint, string login, string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        using var client = new SmtpClient { Timeout = TimeoutMs };
        try
        {
            await ConnectSmtpAsync(client, endpoint, login, password, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return new MailProbeResult(true, "SMTP: вход выполнен, письма отправлять можно.");
        }
        catch (Exception ex) when (Explain(ex, "SMTP", endpoint) is { } message)
        {
            return new MailProbeResult(false, message);
        }
    }

    public async Task<MailProbeResult> SendTestAsync(MailEndpoint endpoint, string login, string password, string address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("labScaner", address));
        message.To.Add(MailboxAddress.Parse(address));
        message.Subject = "labScaner: проверка отправки";
        message.Body = new TextPart("plain")
        {
            Text = "Это пробное письмо из labScaner: отправка уведомлений студентам с этого ящика работает.\n" +
                   "Письмо отправлено только на ваш адрес. Отвечать на него не нужно.",
        };

        using var client = new SmtpClient { Timeout = TimeoutMs };
        try
        {
            await ConnectSmtpAsync(client, endpoint, login, password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return new MailProbeResult(true, $"Пробное письмо отправлено на {address}.");
        }
        catch (Exception ex) when (Explain(ex, "SMTP", endpoint) is { } text)
        {
            return new MailProbeResult(false, text);
        }
    }

    private static async Task ConnectSmtpAsync(SmtpClient client, MailEndpoint endpoint, string login, string password, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(endpoint.Host, endpoint.Port, Options(endpoint.Security), cancellationToken);
        if (client.Capabilities.HasFlag(SmtpCapabilities.Authentication))
        {
            await client.AuthenticateAsync(login, password, cancellationToken);
        }
    }

    private static SecureSocketOptions Options(MailSecurity security) => security switch
    {
        MailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        MailSecurity.StartTls => SecureSocketOptions.StartTls,
        _ => SecureSocketOptions.None,
    };

    /// <summary>Ошибка подключения — понятным текстом; отмену запроса не перехватываем.</summary>
    private static string? Explain(Exception ex, string protocol, MailEndpoint endpoint) => ex switch
    {
        OperationCanceledException => null,
        MailKit.Security.AuthenticationException =>
            $"{protocol}: неверный логин или пароль. У Яндекса и Mail.ru для почтовых программ нужен отдельный «пароль приложения».",
        SslHandshakeException =>
            $"{protocol}: не удалось установить защищённое соединение с {endpoint}. Проверьте порт и тип шифрования (993/465 — SSL, 143/587 — STARTTLS).",
        TimeoutException =>
            $"{protocol}: сервер {endpoint} не ответил за {TimeoutMs / 1000} секунд.",
        SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData } =>
            $"{protocol}: не удалось подключиться к {endpoint} — сервер с таким именем не найден, проверьте адрес сервера.",
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } =>
            $"{protocol}: не удалось подключиться к {endpoint} — сервер не принимает подключения на этом порту.",
        SocketException or IOException =>
            $"{protocol}: не удалось подключиться к {endpoint} — {ex.Message}",
        ProtocolException or CommandException or System.Security.Authentication.AuthenticationException =>
            $"{protocol}: сервер {endpoint} ответил ошибкой — {ex.Message}",
        _ => $"{protocol}: ошибка подключения к {endpoint} — {ex.Message}",
    };
}
