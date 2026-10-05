using System.Net;
using LabScaner.Integration.Tests.Infrastructure;
using LabScaner.Web.Navigation;

namespace LabScaner.Integration.Tests.Web;

[Collection(PostgresTests.Name)]
public sealed class LayoutTests(PostgresFixture postgres)
{
    private const string Password = "teacher-password-1";

    public static TheoryData<string, string> MenuPages()
    {
        var data = new TheoryData<string, string>();
        foreach (var item in NavMenu.Items)
        {
            data.Add(item.Href, item.Label);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MenuPages))]
    public async Task MenuPage_OpensAndIsMarkedActive(string href, string label)
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync(Password);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, Password);

        var response = await client.GetAsync(new Uri(href, UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"href=\"{href}\" aria-current=\"page\"", html, StringComparison.Ordinal);
        Assert.Contains($"<title>{label}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sidebar_ShowsUserNameInitialsAndLogout()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync(Password);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, Password);

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("Преподаватель Тестовый", html, StringComparison.Ordinal);
        Assert.Contains(">ПТ<", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Выйти\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Преподаватель · админ", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/css/site.css")]
    [InlineData("/css/fonts.css")]
    [InlineData("/lib/htmx/htmx.min.js")]
    [InlineData("/lib/fonts/ibm-plex-sans-cyrillic-400-normal.woff2")]
    public async Task StaticAssets_AreServedAnonymously(string path)
    {
        await using var factory = new LabScanerWebFactory(postgres);
        using var client = factory.CreateClientNoRedirect();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
