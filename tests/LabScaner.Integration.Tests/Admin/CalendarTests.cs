using LabScaner.Integration.Tests.Infrastructure;

namespace LabScaner.Integration.Tests.Admin;

[Collection(PostgresTests.Name)]
public sealed class CalendarTests(PostgresFixture postgres)
{
    private const string Password = "admin-password-12";

    private static async Task<string> PostAsync(HttpClient client, string handler, Dictionary<string, string> form)
    {
        var html = await client.GetStringAsync(new Uri("/Admin/Calendar", UriKind.Relative));
        form["__RequestVerificationToken"] = AuthClient.AntiforgeryToken(html);
        var response = await client.PostAsync(new Uri($"/Admin/Calendar?handler={handler}", UriKind.Relative), new FormUrlEncodedContent(form));
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task AddTerm_ThenDuplicateRejected_ThenDatesEdited()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var admin = await factory.CreateTeacherAsync(Password, admin: true);
        using var client = factory.CreateClient();
        await client.LoginAsync(admin.UserName!, Password);
        var year = 2040 + (int)((uint)Guid.NewGuid().GetHashCode() % 50);

        var form = new Dictionary<string, string>
        {
            ["NewTerm.StartYear"] = year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["NewTerm.Season"] = "Autumn",
            ["NewTerm.CreditWeekStart"] = $"{year}-12-21",
            ["NewTerm.SessionStart"] = $"{year + 1}-01-11",
        };
        var added = await PostAsync(client, "Add", new(form));
        Assert.Contains($"Добавлен семестр «{year}-{year + 1}, осенний»", added, StringComparison.Ordinal);

        var duplicate = await PostAsync(client, "Add", new(form));
        Assert.Contains("уже есть", duplicate, StringComparison.Ordinal);

        var id = System.Text.RegularExpressions.Regex.Match(added, $"id=\"term-(\\d+)\"[^>]*>\\s*<td><b>{year}-{year + 1}, осенний").Groups[1].Value;
        Assert.NotEmpty(id);

        var outside = await PostAsync(client, $"Save&id={id}", new() { ["creditWeekStart"] = $"{year + 1}-05-01", ["sessionStart"] = $"{year + 1}-05-20" });
        Assert.Contains("Даты должны попадать в семестр", outside, StringComparison.Ordinal);

        var saved = await PostAsync(client, $"Save&id={id}", new() { ["creditWeekStart"] = $"{year}-12-14", ["sessionStart"] = $"{year + 1}-01-09" });
        Assert.Contains("Сохранено", saved, StringComparison.Ordinal);
        Assert.Contains($"value=\"{year}-12-14\"", saved, StringComparison.Ordinal);
    }
}
