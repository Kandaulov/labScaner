using System.Net;
using System.Text.RegularExpressions;
using LabScaner.Integration.Tests.Infrastructure;
using LabScaner.Web.Prototype;

namespace LabScaner.Integration.Tests.Web;

/// <summary>Прототип журнала (шаг 2.5): htmx-обработчики возвращают нужные фрагменты.</summary>
[Collection(PostgresTests.Name)]
public sealed partial class JournalPrototypeTests(PostgresFixture postgres)
{
    private const string Password = "teacher-password-1";
    private const string Page = "/Prototype/Journal";

    private sealed class Session(LabScanerWebFactory factory, HttpClient client, string token) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public string Token { get; } = token;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await factory.DisposeAsync();
        }
    }

    private static async Task<Session> OpenAsync(PostgresFixture postgres)
    {
        var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync(Password);
        var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, Password);
        var html = await client.GetStringAsync(new Uri(Page, UriKind.Relative));
        return new Session(factory, client, HxToken().Match(html).Groups[1].Value);
    }

    private static Task<HttpResponseMessage> HxPostAsync(HttpClient client, string token, string url, Dictionary<string, string> form)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url, UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Add("RequestVerificationToken", token);
        request.Headers.Add("HX-Request", "true");
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Page_Shows25StudentsAnd8Labs()
    {
        await using var session = await OpenAsync(postgres);
        var client = session.Client;

        var html = await client.GetStringAsync(new Uri(Page, UriKind.Relative));

        Assert.Equal(25, Regex.Count(html, "class=\"jt__check\""));
        Assert.Contains("Лаб 8", html, StringComparison.Ordinal);
        Assert.Contains("Прототип на тестовых данных", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QuickMark_ReturnsAcceptedCellAndUpdatesTotals()
    {
        await using var session = await OpenAsync(postgres);
        var (client, token) = (session.Client, session.Token);

        // Гордеева (4), Лаб 4 — ждёт проверки на макете.
        var response = await HxPostAsync(client, token, $"{Page}?handler=Mark&s=4&l=4", new() { ["grade"] = "5" });
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<td id=\"c4-4\"", html.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("cell--ok", html, StringComparison.Ordinal);
        Assert.Contains("id=\"t4\" hx-swap-oob=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"a4\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_WithoutAntiforgeryToken_IsRejected()
    {
        await using var session = await OpenAsync(postgres);
        var client = session.Client;

        var response = await HxPostAsync(client, "wrong", $"{Page}?handler=Mark&s=4&l=4", new() { ["grade"] = "5" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Panel_ReturnDecision_UpdatesPanelAndCell()
    {
        await using var session = await OpenAsync(postgres);
        var (client, token) = (session.Client, session.Token);

        var panel = await client.GetStringAsync(new Uri($"{Page}?handler=Panel&s=9&l=3", UriKind.Relative));
        Assert.Contains("Иванов Иван Иванович — Лаб №3", panel, StringComparison.Ordinal);
        Assert.Contains("Предварительная проверка — предложение", panel, StringComparison.Ordinal);

        var response = await HxPostAsync(client, token, $"{Page}?handler=Decide&s=9&l=3", new()
        {
            ["decision"] = "return",
            ["remarks"] = "Добавьте выводы",
            ["notify"] = "true",
        });
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Сохранено", html, StringComparison.Ordinal);
        Assert.Contains("id=\"c9-3\"", html, StringComparison.Ordinal);
        Assert.Contains("cell--back", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Note_AppearsInComments()
    {
        await using var session = await OpenAsync(postgres);
        var (client, token) = (session.Client, session.Token);

        var response = await HxPostAsync(client, token, $"{Page}?handler=Note&s=9&l=3", new() { ["note"] = "Спросить на защите про TO-BE" });

        Assert.Contains("Спросить на защите про TO-BE", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutomatFilter_ShowsOnlyCandidates_AndAutoApplyGivesAutomat()
    {
        await using var session = await OpenAsync(postgres);
        var (client, token) = (session.Client, session.Token);

        var filtered = await client.GetStringAsync(new Uri($"{Page}?filter=automat", UriKind.Relative));
        var ids = Regex.Matches(filtered, "name=\"selected\" value=\"(\\d+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(ids);

        var dialog = await HxPostAsync(client, token, $"{Page}?handler=AutoDialog", new() { ["selected"] = ids[0] });
        Assert.Contains("Поставить автомат «5 (отлично)»?", await dialog.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var apply = await HxPostAsync(client, token, $"{Page}?handler=AutoApply", new() { ["selected"] = ids[0] });
        Assert.Equal("true", apply.Headers.GetValues("HX-Refresh").Single());

        var page = await client.GetStringAsync(new Uri(Page, UriKind.Relative));
        Assert.Contains($"id=\"a{ids[0]}\"", page, StringComparison.Ordinal);
        Assert.Matches($"id=\"a{ids[0]}\" class=\"jt__auto\">\\s*<span class=\"auto auto--given\">", page);
    }

    [Fact]
    public void AutomatRule_LateLabOrMissingCoursework_IsNotCandidate()
    {
        var store = new JournalPrototypeStore();
        var candidate = store.Students.First(s => JournalPrototypeStore.Automat(s) == AutomatState.Candidate);

        candidate.Labs[0].AcceptedAt = new DateTime(2026, 12, 22);
        Assert.Equal(AutomatState.None, JournalPrototypeStore.Automat(candidate));

        candidate.Labs[0].AcceptedAt = new DateTime(2026, 12, 1);
        candidate.Coursework.Status = CellStatus.AwaitingReview;
        Assert.Equal(AutomatState.WaitsCoursework, JournalPrototypeStore.Automat(candidate));
    }

    [GeneratedRegex("\"RequestVerificationToken\": \"([^\"]+)\"")]
    private static partial Regex HxToken();
}
