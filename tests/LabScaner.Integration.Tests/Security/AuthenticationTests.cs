using System.Net;
using LabScaner.Integration.Tests.Infrastructure;
using LabScaner.Web.Pages.Account;

namespace LabScaner.Integration.Tests.Security;

[Collection(PostgresTests.Name)]
public sealed class AuthenticationTests(PostgresFixture postgres)
{
    private const string Password = "teacher-password-1";

    [Fact]
    public async Task Anonymous_RedirectedToLogin()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        using var client = factory.CreateClientNoRedirect();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location?.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BootstrapAdmin_CanLogIn_AndSeesAdminSettings()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        using var client = factory.CreateClient();

        var response = await client.LoginAsync(LabScanerWebFactory.AdminLogin, LabScanerWebFactory.AdminPassword);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(LabScanerWebFactory.AdminDisplayName, html, StringComparison.Ordinal);
        Assert.Contains("Преподаватель · админ", html, StringComparison.Ordinal);

        var settings = await client.GetStringAsync(new Uri("/Settings", UriKind.Relative));
        Assert.Contains("id=\"admin-h\"", settings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Teacher_DoesNotSeeAdminSettings()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync(Password);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, Password);

        var settings = await client.GetStringAsync(new Uri("/Settings", UriKind.Relative));

        Assert.DoesNotContain("id=\"admin-h\"", settings, StringComparison.Ordinal);
        Assert.Contains("Мои настройки", settings, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongPassword_ShowsError()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync(Password);
        using var client = factory.CreateClient();

        var response = await client.LoginAsync(teacher.UserName!, "wrong-password");

        Assert.Contains(LoginModel.WrongCredentials, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FiveWrongPasswords_LockAccount_EvenForCorrectPassword()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync(Password);
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            await client.LoginAsync(teacher.UserName!, "wrong-password");
        }

        var response = await client.LoginAsync(teacher.UserName!, Password);

        Assert.Contains(LoginModel.LockedOut, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Teacher_CannotOpenAdmin()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync(Password);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, Password);

        var html = await client.GetStringAsync(new Uri("/Admin", UriKind.Relative));

        Assert.Contains("Нет доступа", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_CanOpenAdmin()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var admin = await factory.CreateTeacherAsync(Password, admin: true);
        using var client = factory.CreateClientNoRedirect();
        await client.LoginAsync(admin.UserName!, Password);

        var response = await client.GetAsync(new Uri("/Admin", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Logout_EndsSession()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync(Password);
        using var client = factory.CreateClient();
        var home = await (await client.LoginAsync(teacher.UserName!, Password)).Content.ReadAsStringAsync();

        await client.PostAsync(new Uri("/Account/Logout", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AuthClient.AntiforgeryToken(home),
        }));

        var after = await client.GetAsync(new Uri("/", UriKind.Relative));
        Assert.Contains("Войти", await after.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginRequests_AreRateLimited()
    {
        await using var factory = new LabScanerWebFactory(postgres, loginRequestsPerMinute: 3);
        using var client = factory.CreateClientNoRedirect();

        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 4; i++)
        {
            last = (await client.GetAsync(new Uri("/Account/Login", UriKind.Relative))).StatusCode;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, last);
    }
}
