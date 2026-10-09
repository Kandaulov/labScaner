using LabScaner.Integration.Tests.Infrastructure;

namespace LabScaner.Integration.Tests.Directory;

[Collection(PostgresTests.Name)]
public sealed class SubjectsPageTests(PostgresFixture postgres)
{
    private const string Password = "teacher-password-1";

    private static async Task<string> PostAsync(HttpClient client, string url, Dictionary<string, string> form)
    {
        var page = await client.GetStringAsync(new Uri("/Subjects", UriKind.Relative));
        form["__RequestVerificationToken"] = AuthClient.AntiforgeryToken(page);
        var response = await client.PostAsync(new Uri(url, UriKind.Relative), new FormUrlEncodedContent(form));
        return await response.Content.ReadAsStringAsync();
    }

    private static Dictionary<string, string> Korpis(string aliases = "КИС, Корп ИС") => new()
    {
        ["Input.Name"] = "Корпоративные информационные системы",
        ["Input.Code"] = "КорпИС",
        ["Input.Aliases"] = aliases,
        ["Input.FinalAssessment"] = "Exam",
        ["Input.AiReferenceText"] = "Нотации: BPMN 2.0, IDEF0.",
        ["Input.DiskPathTemplate"] = @"30 Политех\03 {Код}\10 {Код} - Отчетные документы\{Учебный год} {Код} {Направление} - {N} сем",
    };

    [Fact]
    public async Task Teacher_CreatesSubject_SeesPathPreview_AndOthersDoNotSeeIt()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync(Password);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, Password);

        var empty = await client.GetStringAsync(new Uri("/Subjects", UriKind.Relative));
        Assert.Contains("Новый предмет", empty, StringComparison.Ordinal);

        var created = await PostAsync(client, "/Subjects?handler=Save", Korpis());
        Assert.Contains("Предмет «КорпИС» добавлен", created, StringComparison.Ordinal);
        Assert.Contains("value=\"КИС\"", created, StringComparison.Ordinal); // «Корп ИС» совпадает с кодом и отброшен
        Assert.Contains("2026-2027 КорпИС ИСТ - 7 сем", created, StringComparison.Ordinal);
        Assert.Contains("30 Политех/03 {Код}", created, StringComparison.Ordinal);

        // Другой преподаватель предмета не видит.
        var colleague = await factory.CreateTeacherAsync(Password);
        using var other = factory.CreateClient();
        await other.LoginAsync(colleague.UserName!, Password);
        var otherPage = await other.GetStringAsync(new Uri("/Subjects", UriKind.Relative));
        Assert.DoesNotContain("Корпоративные информационные системы", otherPage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConflictingSpelling_IsRejected()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync(Password);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, Password);
        await PostAsync(client, "/Subjects?handler=Save", Korpis());

        var second = await PostAsync(client, "/Subjects?handler=Save", new()
        {
            ["Input.Name"] = "Компьютерные информационные системы",
            ["Input.Code"] = "кис",
            ["Input.FinalAssessment"] = "Pass",
            ["Input.DiskPathTemplate"] = "{Код}",
        });

        Assert.Contains("уже используется предметом «КорпИС»", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BadTemplate_ShowsError_AndPreviewEndpointExplains()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync(Password);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, Password);

        var form = Korpis();
        form["Input.DiskPathTemplate"] = "{Предмет}/{N}";
        var saved = await PostAsync(client, "/Subjects?handler=Save", form);
        Assert.Contains("Неизвестные подстановки: {Предмет}", saved, StringComparison.Ordinal);

        var preview = await PostAsync(client, "/Subjects?handler=PathPreview", new() { ["Input.Code"] = "ОС", ["Input.DiskPathTemplate"] = "{Код}/{N} сем" });
        Assert.Contains("ОС", preview, StringComparison.Ordinal);
        Assert.Contains("7 сем", preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Archive_AndRestore()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync(Password);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, Password);
        var created = await PostAsync(client, "/Subjects?handler=Save", Korpis());
        var id = System.Text.RegularExpressions.Regex.Match(created, "href=\"\\?id=(\\d+)\"").Groups[1].Value;

        var archived = await PostAsync(client, $"/Subjects?handler=Archive&id={id}&restore=false", []);
        Assert.Contains("перенесён в архив", archived, StringComparison.Ordinal);
        Assert.Contains("Вернуть из архива", archived, StringComparison.Ordinal);

        var restored = await PostAsync(client, $"/Subjects?handler=Archive&id={id}&restore=true", []);
        Assert.Contains("снова в списке", restored, StringComparison.Ordinal);
    }
}
