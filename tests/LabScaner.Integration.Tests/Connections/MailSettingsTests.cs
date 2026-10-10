using System.Net;
using LabScaner.Core.Connections;
using LabScaner.Infrastructure.Security;
using LabScaner.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Integration.Tests.Connections;

/// <summary>Шаг 3.6а: почтовый ящик преподавателя — сохранение, шифрование пароля, проверка IMAP/SMTP на GreenMail.</summary>
[Collection(PostgresTests.Name)]
public sealed class MailSettingsTests(PostgresFixture postgres, GreenMailFixture mail) : IClassFixture<GreenMailFixture>
{
    private const string TeacherPassword = "teacher-password-1";

    private static async Task<string> PostAsync(HttpClient client, string handler, Dictionary<string, string> form)
    {
        var page = await client.GetStringAsync(new Uri("/Settings/Mail", UriKind.Relative));
        form["__RequestVerificationToken"] = AuthClient.AntiforgeryToken(page);
        var response = await client.PostAsync(new Uri($"/Settings/Mail?handler={handler}", UriKind.Relative), new FormUrlEncodedContent(form));
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private Dictionary<string, string> Form(string password, int? imapPort = null) => new()
    {
        ["Input.Address"] = GreenMailFixture.Address,
        ["Input.Login"] = GreenMailFixture.Login,
        ["Input.Password"] = password,
        ["Input.ImapHost"] = mail.Host,
        ["Input.ImapPort"] = (imapPort ?? mail.ImapPort).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Input.ImapSecurity"] = nameof(MailSecurity.None),
        ["Input.SmtpHost"] = mail.Host,
        ["Input.SmtpPort"] = mail.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Input.SmtpSecurity"] = nameof(MailSecurity.None),
        ["Input.ReadSince"] = "2026-09-01",
    };

    [Fact]
    public async Task Teacher_ConnectsMailbox_PasswordEncrypted_ChecksAndSendsTest_OthersDoNotSee()
    {
        var database = await postgres.CreateEmptyDatabaseAsync();
        await using var factory = new LabScanerWebFactory(postgres, connectionString: database);
        var teacher = await factory.CreateTeacherAsync(TeacherPassword);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, TeacherPassword);

        var empty = await client.GetStringAsync(new Uri("/Settings/Mail", UriKind.Relative));
        Assert.Contains("Подключить почтовый ящик", empty, StringComparison.Ordinal);
        Assert.Contains("Почта не подключена", empty, StringComparison.Ordinal);

        // Без пароля новый ящик не сохраняется.
        var noPassword = await PostAsync(client, "Save", Form(string.Empty));
        Assert.Contains("Введите пароль ящика", noPassword, StringComparison.Ordinal);

        var saved = await PostAsync(client, "Save", Form(GreenMailFixture.Password));
        Assert.Contains("Почта подключена.", saved, StringComparison.Ordinal);
        Assert.Contains("IMAP: вход выполнен", saved, StringComparison.Ordinal);
        Assert.Contains("SMTP: вход выполнен", saved, StringComparison.Ordinal);
        Assert.Contains($"Ящик {GreenMailFixture.Address} подключён", saved, StringComparison.Ordinal);
        Assert.DoesNotContain(GreenMailFixture.Password, saved, StringComparison.Ordinal);

        await using (var db = postgres.CreateDbContext(new FixedTeacher(teacher.Id), database))
        {
            var row = await db.MailConnections.SingleAsync();
            Assert.DoesNotContain(GreenMailFixture.Password, row.PasswordProtected, StringComparison.Ordinal);
            Assert.True(row.LastCheckOk);
            Assert.Equal(new DateOnly(2026, 9, 1), row.ReadSince);
        }

        // Пробное письмо уходит самому себе и появляется во «Входящих».
        var sent = await PostAsync(client, "SendTest", []);
        Assert.Contains($"Пробное письмо отправлено на {GreenMailFixture.Address}", sent, StringComparison.Ordinal);
        var check = await PostAsync(client, "Check", []);
        Assert.Contains("во «Входящих» 1 писем", check, StringComparison.Ordinal);

        // Пустой пароль при сохранении — прежний пароль остаётся.
        var resaved = await PostAsync(client, "Save", Form(string.Empty));
        Assert.Contains("Настройки сохранены.", resaved, StringComparison.Ordinal);
        Assert.Contains("IMAP: вход выполнен", resaved, StringComparison.Ordinal);

        // Другой преподаватель ящика не видит.
        var colleague = await factory.CreateTeacherAsync(TeacherPassword);
        using var other = factory.CreateClient();
        await other.LoginAsync(colleague.UserName!, TeacherPassword);
        var otherPage = await other.GetStringAsync(new Uri("/Settings/Mail", UriKind.Relative));
        Assert.Contains("Подключить почтовый ящик", otherPage, StringComparison.Ordinal);
        Assert.DoesNotContain(GreenMailFixture.Address, otherPage, StringComparison.Ordinal);

        // Отключение удаляет подключение.
        var disconnected = await PostAsync(client, "Disconnect", []);
        Assert.Contains("Почта отключена", disconnected, StringComparison.Ordinal);
        Assert.Contains("Почта не подключена", disconnected, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongPasswordOrClosedPort_ShowReadableErrors()
    {
        var database = await postgres.CreateEmptyDatabaseAsync();
        await using var factory = new LabScanerWebFactory(postgres, connectionString: database);
        var teacher = await factory.CreateTeacherAsync(TeacherPassword);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, TeacherPassword);

        var wrong = await PostAsync(client, "Save", Form("wrong-password"));
        Assert.Contains("IMAP: неверный логин или пароль", wrong, StringComparison.Ordinal);
        Assert.Contains($"Ящик {GreenMailFixture.Address} не отвечает", wrong, StringComparison.Ordinal);

        var closed = await PostAsync(client, "Save", Form(GreenMailFixture.Password, imapPort: 1));
        Assert.Contains("IMAP: не удалось подключиться", closed, StringComparison.Ordinal);

        var badHost = Form(GreenMailFixture.Password);
        badHost["Input.ImapHost"] = "https://imap.yandex.ru";
        var invalid = await PostAsync(client, "Save", badHost);
        Assert.Contains("только имя, например imap.yandex.ru", invalid, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretProtector_RoundTrip_AndLostKeysGiveNull()
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var other = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());

        var protectedSecret = protector.Protect("пароль-приложения");

        Assert.NotEqual("пароль-приложения", protectedSecret);
        Assert.Equal("пароль-приложения", protector.Unprotect(protectedSecret));
        Assert.Null(other.Unprotect(protectedSecret));
        Assert.Null(protector.Unprotect("not-a-protected-value"));
    }

    private sealed class FixedTeacher(int id) : LabScaner.Core.Abstractions.ICurrentTeacher
    {
        public int? TeacherId => id;
    }
}
