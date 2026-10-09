using LabScaner.Integration.Tests.Infrastructure;

namespace LabScaner.Integration.Tests.Directory;

[Collection(PostgresTests.Name)]
public sealed class GroupsPageTests(PostgresFixture postgres)
{
    private const string Password = "admin-password-12";

    private static async Task<string> PostAsync(HttpClient client, string url, Dictionary<string, string> form)
    {
        var page = await client.GetStringAsync(new Uri("/Groups", UriKind.Relative));
        form["__RequestVerificationToken"] = AuthClient.AntiforgeryToken(page);
        var response = await client.PostAsync(new Uri(url, UriKind.Relative), new FormUrlEncodedContent(form));
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Admin_AddsGroupStudentEmail_ThenDeactivatesStudent()
    {
        var database = await postgres.CreateEmptyDatabaseAsync();
        await using var factory = new LabScanerWebFactory(postgres, connectionString: database);
        var admin = await factory.CreateTeacherAsync(Password, admin: true);
        using var client = factory.CreateClient();
        await client.LoginAsync(admin.UserName!, Password);

        var created = await PostAsync(client, "/Groups?handler=AddGroup", new() { ["name"] = "ист43" });
        Assert.Contains("Добавлена группа ИСТ-43", created, StringComparison.Ordinal);
        var groupId = System.Text.RegularExpressions.Regex.Match(created, "href=\"\\?group=(\\d+)\">ИСТ-43").Groups[1].Value;

        var duplicate = await PostAsync(client, "/Groups?handler=AddGroup", new() { ["name"] = "ИСТ-43" });
        Assert.Contains("уже есть", duplicate, StringComparison.Ordinal);

        var student = await PostAsync(client, $"/Groups?handler=AddStudent&group={groupId}", new() { ["fullName"] = "петров пётр петрович" });
        Assert.Contains("Добавлен студент Петров Пётр Петрович", student, StringComparison.Ordinal);
        var studentId = System.Text.RegularExpressions.Regex.Match(student, "handler=AddEmail&amp;id=(\\d+)").Groups[1].Value;

        var email = await PostAsync(client, $"/Groups?handler=AddEmail&id={studentId}&group={groupId}", new() { ["email"] = "Petrov@Mail.ru" });
        Assert.Contains("petrov@mail.ru", email, StringComparison.Ordinal);

        var bad = await PostAsync(client, $"/Groups?handler=AddEmail&id={studentId}&group={groupId}", new() { ["email"] = "petrov" });
        Assert.Contains("Некорректный e-mail", bad, StringComparison.Ordinal);

        var off = await PostAsync(client, $"/Groups?handler=SetActive&id={studentId}&active=false&group={groupId}", []);
        Assert.Contains("отчисленный", off, StringComparison.Ordinal);
        Assert.Contains("В группе нет студентов", off, StringComparison.Ordinal);

        var withInactive = await client.GetStringAsync(new Uri($"/Groups?group={groupId}&inactive=true", UriKind.Relative));
        Assert.Contains("Петров Пётр Петрович", withInactive, StringComparison.Ordinal);
        Assert.Contains("отчислен", withInactive, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_FindsByNameIgnoringCaseAndYo()
    {
        var database = await postgres.CreateEmptyDatabaseAsync();
        await using var factory = new LabScanerWebFactory(postgres, connectionString: database);
        var admin = await factory.CreateTeacherAsync(Password, admin: true);
        using var client = factory.CreateClient();
        await client.LoginAsync(admin.UserName!, Password);
        var created = await PostAsync(client, "/Groups?handler=AddGroup", new() { ["name"] = "ИСТ-44" });
        var groupId = System.Text.RegularExpressions.Regex.Match(created, "href=\"\\?group=(\\d+)\">ИСТ-44").Groups[1].Value;
        await PostAsync(client, $"/Groups?handler=AddStudent&group={groupId}", new() { ["fullName"] = "Журавлёв Семён Павлович" });
        await PostAsync(client, $"/Groups?handler=AddStudent&group={groupId}", new() { ["fullName"] = "Сидоров Олег" });

        var found = await client.GetStringAsync(new Uri($"/Groups?group={groupId}&q=журавлев", UriKind.Relative));

        Assert.Contains("Журавлёв Семён Павлович", found, StringComparison.Ordinal);
        Assert.DoesNotContain("Сидоров Олег", found, StringComparison.Ordinal);
    }
}
