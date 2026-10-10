using LabScaner.Core.Connections;

namespace LabScaner.Core.Tests.Connections;

public sealed class MailConnectionTests
{
    private static readonly MailEndpoint _imap = new("imap.yandex.ru", 993, MailSecurity.SslOnConnect);
    private static readonly MailEndpoint _smtp = new("smtp.yandex.ru", 465, MailSecurity.SslOnConnect);

    private static MailConnection Create(string? login = null) =>
        new("v.ivanov@ulstu.ru", login, _imap, _smtp, new DateOnly(2026, 9, 1));

    [Fact]
    public void Login_DefaultsToAddress_HostsNormalized()
    {
        var c = new MailConnection(" v.ivanov@ulstu.ru ", " ", new MailEndpoint(" IMAP.Yandex.RU ", 993, MailSecurity.SslOnConnect), _smtp, new DateOnly(2026, 9, 1));

        Assert.Equal("v.ivanov@ulstu.ru", c.Login);
        Assert.Equal("imap.yandex.ru", c.ImapHost);
        Assert.False(c.HasPassword);
    }

    [Theory]
    [InlineData("не адрес", "imap.yandex.ru", 993)]
    [InlineData("v.ivanov@ulstu.ru", "", 993)]
    [InlineData("v.ivanov@ulstu.ru", "https://imap.yandex.ru", 993)]
    [InlineData("v.ivanov@ulstu.ru", "imap.yandex.ru:993", 993)]
    [InlineData("v.ivanov@ulstu.ru", "imap.yandex.ru", 0)]
    [InlineData("v.ivanov@ulstu.ru", "imap.yandex.ru", 70000)]
    public void Invalid_Throws(string address, string imapHost, int imapPort) =>
        Assert.Throws<ArgumentException>(() => new MailConnection(address, null, new MailEndpoint(imapHost, imapPort, MailSecurity.SslOnConnect), _smtp, new DateOnly(2026, 9, 1)));

    [Fact]
    public void ChangingMailbox_ResetsReadPosition_ButOtherEditsKeepIt()
    {
        var c = Create();
        typeof(MailConnection).GetProperty(nameof(MailConnection.ImapLastUid))!.SetValue(c, 120L);

        c.Update("v.ivanov@ulstu.ru", null, _imap, new MailEndpoint("smtp.mail.ru", 465, MailSecurity.SslOnConnect), new DateOnly(2026, 9, 15));
        Assert.Equal(120L, c.ImapLastUid);

        c.Update("v.ivanov@ulstu.ru", "other-login", _imap, _smtp, new DateOnly(2026, 9, 15));
        Assert.Null(c.ImapLastUid);
    }

    [Fact]
    public void RecordCheck_TruncatesLongMessage()
    {
        var c = Create();

        c.RecordCheck(DateTimeOffset.UnixEpoch, false, new string('ы', 800));

        Assert.False(c.LastCheckOk);
        Assert.Equal(500, c.LastCheckMessage!.Length);
    }

    [Theory]
    [InlineData("ivanov@yandex.ru", "imap.yandex.ru", "smtp.yandex.ru")]
    [InlineData("ivanov@BK.ru", "imap.mail.ru", "smtp.mail.ru")]
    [InlineData("ivanov@gmail.com", "imap.gmail.com", "smtp.gmail.com")]
    public void Presets_KnownDomains(string address, string imap, string smtp)
    {
        var preset = MailPresets.Guess(address);

        Assert.NotNull(preset);
        Assert.Equal(imap, preset.Value.Imap.Host);
        Assert.Equal(993, preset.Value.Imap.Port);
        Assert.Equal(smtp, preset.Value.Smtp.Host);
        Assert.Equal(465, preset.Value.Smtp.Port);
    }

    [Fact]
    public void Presets_UnknownDomain_Null() => Assert.Null(MailPresets.Guess("v.ivanov@ulstu.ru"));
}
