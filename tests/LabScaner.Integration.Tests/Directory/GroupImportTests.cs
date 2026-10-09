using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using LabScaner.Infrastructure.Import;
using LabScaner.Integration.Tests.Infrastructure;

namespace LabScaner.Integration.Tests.Directory;

[Collection(PostgresTests.Name)]
public sealed partial class GroupImportTests(PostgresFixture postgres)
{
    private const string Password = "admin-password-12";
    private static readonly string _fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "groups", "groups-yandex-tables-synthetic.xlsx");

    [Fact]
    public void XlsxReader_ReadsYandexTablesFile_WithNonStandardStyles()
    {
        using var file = File.OpenRead(_fixture);

        var sheets = XlsxReader.Read(file);

        Assert.Equal(["ИСТ41", "ИСТ42"], sheets.Select(s => s.Name));
        Assert.Equal("ФИО", sheets[0].Rows[0][1]);
        Assert.Equal("НАЗАРОВ ОЛЕГ ИГОРЕВИЧ", sheets[0].Rows[1][1]);
        Assert.Equal(23, sheets[0].Rows.Count);
        Assert.Equal(20, sheets[1].Rows.Count);
    }

    [Fact]
    public void XlsxReader_NotAnXlsx_ThrowsReadableError()
    {
        using var file = new MemoryStream("ФИО;Тема"u8.ToArray());

        var error = Assert.Throws<InvalidDataException>(() => XlsxReader.Read(file));

        Assert.Contains("Excel", error.Message, StringComparison.Ordinal);
    }

    private static async Task<string> UploadAsync(HttpClient client)
    {
        var page = await client.GetStringAsync(new Uri("/Groups/Import", UriKind.Relative));
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(AuthClient.AntiforgeryToken(page)), "__RequestVerificationToken");
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(_fixture));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "groups.xlsx");
        var response = await client.PostAsync(new Uri("/Groups/Import?handler=Preview", UriKind.Relative), form);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<string> ApplyAsync(HttpClient client, string preview, params string[] groups)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", AuthClient.AntiforgeryToken(preview)),
            new("payload", System.Net.WebUtility.HtmlDecode(Payload().Match(preview).Groups[1].Value)),
        };
        form.AddRange(groups.Select(g => new KeyValuePair<string, string>("groups", g)));
        var response = await client.PostAsync(new Uri("/Groups/Import?handler=Apply", UriKind.Relative), new FormUrlEncodedContent(form));
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Import_PreviewThenApply_ThenReimportChangesNothing()
    {
        var database = await postgres.CreateEmptyDatabaseAsync();
        await using var factory = new LabScanerWebFactory(postgres, connectionString: database);
        var admin = await factory.CreateTeacherAsync(Password, admin: true);
        using var client = factory.CreateClient();
        await client.LoginAsync(admin.UserName!, Password);

        var preview = await UploadAsync(client);
        Assert.Contains("новых 22", preview, StringComparison.Ordinal);
        Assert.Contains("новых 19", preview, StringComparison.Ordinal);
        Assert.Contains("Блинова Ульяна Борисовна", preview, StringComparison.Ordinal);
        Assert.Contains("Морской порт", preview, StringComparison.Ordinal);

        // Импортируем только ИСТ-41.
        var applied = await ApplyAsync(client, preview, "ИСТ-41");
        Assert.Contains("новых студентов — 22", applied, StringComparison.Ordinal);

        var groups = await client.GetStringAsync(new Uri("/Groups", UriKind.Relative));
        Assert.Contains("ИСТ-41 · 22", groups, StringComparison.Ordinal);
        Assert.DoesNotContain("ИСТ-42", groups, StringComparison.Ordinal);
        Assert.Contains("Журавлёв Семён Павлович", groups, StringComparison.Ordinal);

        var again = await UploadAsync(client);
        Assert.Contains("уже есть 22", again, StringComparison.Ordinal);
        Assert.Contains("новых 19", again, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_TamperedPayload_IsRejected()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var admin = await factory.CreateTeacherAsync(Password, admin: true);
        using var client = factory.CreateClient();
        await client.LoginAsync(admin.UserName!, Password);
        var page = await client.GetStringAsync(new Uri("/Groups/Import", UriKind.Relative));

        var response = await client.PostAsync(new Uri("/Groups/Import?handler=Apply", UriKind.Relative), new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", AuthClient.AntiforgeryToken(page)),
            new("payload", "[{\"GroupName\":\"ИСТ-99\",\"Rows\":[]}]"),
            new("groups", "ИСТ-99"),
        ]));

        Assert.Contains("Предпросмотр устарел или повреждён", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Teacher_SeesGroupsReadOnly_AndCannotImport()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync("teacher-password-1");
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, "teacher-password-1");

        var groups = await client.GetStringAsync(new Uri("/Groups", UriKind.Relative));
        Assert.Contains("Только просмотр", groups, StringComparison.Ordinal);
        Assert.DoesNotContain("Импорт из Excel", groups, StringComparison.Ordinal);

        var import = await client.GetStringAsync(new Uri("/Groups/Import", UriKind.Relative));
        Assert.Contains("Раздел только для администратора", import, StringComparison.Ordinal);
    }

    [GeneratedRegex("name=\"payload\" value=\"([^\"]+)\"")]
    private static partial Regex Payload();
}
