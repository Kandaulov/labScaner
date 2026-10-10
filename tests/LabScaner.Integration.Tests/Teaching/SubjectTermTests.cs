using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using LabScaner.Core.Directory;
using LabScaner.Infrastructure.Import;
using LabScaner.Integration.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LabScaner.Integration.Tests.Teaching;

/// <summary>Шаг 3.4: предмет в семестре, работы и дедлайны, темы курсовых, журнал группы.</summary>
[Collection(PostgresTests.Name)]
public sealed partial class SubjectTermTests(PostgresFixture postgres)
{
    private const string Password = "teacher-password-1";
    private static readonly string _fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "groups", "groups-yandex-tables-synthetic.xlsx");

    /// <summary>Семестр всегда в будущем — тест не зависит от сегодняшней даты.</summary>
    private static readonly int _year = DateTime.UtcNow.Year + 1;

    private sealed record Seed(int TermId, int Ist41, int Ist42);

    private static async Task<Seed> SeedAsync(PostgresFixture postgres, string database)
    {
        await using var db = postgres.CreateDbContext(database);
        var term = new Term(_year, TermSeason.Autumn, new DateOnly(_year, 12, 21), new DateOnly(_year + 1, 1, 11));
        var ist41 = new Group("ИСТ-41");
        ist41.AddStudent(PersonName.Parse("Морозов Иван Петрович"));
        ist41.AddStudent(PersonName.Parse("Белова Анна Сергеевна"));
        var ist42 = new Group("ИСТ-42");
        ist42.AddStudent(PersonName.Parse("Кузнецов Олег Олегович"));
        db.Terms.Add(term);
        db.Groups.AddRange(ist41, ist42);
        await db.SaveChangesAsync();
        return new Seed(term.Id, ist41.Id, ist42.Id);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, IEnumerable<KeyValuePair<string, string>> form)
    {
        var page = await client.GetStringAsync(new Uri("/Subjects", UriKind.Relative));
        var fields = form.Append(new KeyValuePair<string, string>("__RequestVerificationToken", AuthClient.AntiforgeryToken(page)));
        return await client.PostAsync(new Uri(url, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private static async Task<string> PostTextAsync(HttpClient client, string url, IEnumerable<KeyValuePair<string, string>> form) =>
        await (await PostAsync(client, url, form)).Content.ReadAsStringAsync();

    private static async Task<string> CreateSubjectAsync(HttpClient client)
    {
        var created = await PostTextAsync(client, "/Subjects?handler=Save", new Dictionary<string, string>
        {
            ["Input.Name"] = "Корпоративные информационные системы",
            ["Input.Code"] = "КорпИС",
            ["Input.FinalAssessment"] = "Exam",
            ["Input.DiskPathTemplate"] = "30 Политех/{Код}/{Учебный год} {Код} {Направление} - {N} сем",
        });
        return SubjectId().Match(created).Groups[1].Value;
    }

    /// <summary>Заводит предмет в семестре и возвращает его id и HTML страницы семестра.</summary>
    private static async Task<(string Id, string Html)> AddTermAsync(HttpClient client, string subjectId, Seed seed, int labs, bool coursework, params int[] groups)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("NewTerm.TermId", seed.TermId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("NewTerm.StudySemester", "7"),
            new("NewTerm.Labs", labs.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        if (coursework)
        {
            form.Add(new("NewTerm.Coursework", "true"));
        }

        form.AddRange(groups.Select(g => new KeyValuePair<string, string>("NewTerm.GroupIds", g.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        var response = await PostAsync(client, $"/Subjects?handler=AddTerm&id={subjectId}", form);
        var html = await response.Content.ReadAsStringAsync();
        var id = TermPageId().Match(response.RequestMessage!.RequestUri!.ToString()).Groups[1].Value;
        return (id, html);
    }

    [Fact]
    public async Task Teacher_SetsUpSubjectInTerm_AndSeesEmptyJournal_OthersDoNot()
    {
        var database = await postgres.CreateEmptyDatabaseAsync();
        await using var factory = new LabScanerWebFactory(postgres, connectionString: database);
        var teacher = await factory.CreateTeacherAsync(Password);
        var seed = await SeedAsync(postgres, database);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, Password);
        var subjectId = await CreateSubjectAsync(client);

        var subjectPage = await client.GetStringAsync(new Uri($"/Subjects?id={subjectId}", UriKind.Relative));
        Assert.Contains("+ Семестр", subjectPage, StringComparison.Ordinal);
        Assert.Contains($"{_year}-{_year + 1}, осенний", subjectPage, StringComparison.Ordinal);

        // Создание: путь по шаблону, лабы и курсовая, группа.
        var (stId, termPage) = await AddTermAsync(client, subjectId, seed, labs: 3, coursework: true, seed.Ist41);
        Assert.NotEmpty(stId);
        Assert.Contains($"30 Политех/КорпИС/{_year}-{_year + 1} КорпИС ИСТ - 7 сем", termPage, StringComparison.Ordinal);
        Assert.Contains("Лаб №3", termPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Лаб №4", termPage, StringComparison.Ordinal);
        Assert.Contains("Темы курсовых", termPage, StringComparison.Ordinal);
        Assert.Contains($"из календаря: 21.12.{_year}", termPage, StringComparison.Ordinal);
        Assert.Contains($"из календаря: 11.01.{_year + 1}", termPage, StringComparison.Ordinal);

        // Тот же семестр второй раз не заводится.
        var (_, duplicate) = await AddTermAsync(client, subjectId, seed, labs: 1, coursework: false, seed.Ist41);
        Assert.Contains("уже заведён", duplicate, StringComparison.Ordinal);

        // Без групп — нельзя.
        int springId;
        await using (var db = postgres.CreateDbContext(database))
        {
            var spring = new Term(_year, TermSeason.Spring, new DateOnly(_year + 1, 5, 25), new DateOnly(_year + 1, 6, 8));
            db.Terms.Add(spring);
            await db.SaveChangesAsync();
            springId = spring.Id;
        }

        var noGroups = await PostTextAsync(client, $"/Subjects?handler=AddTerm&id={subjectId}", new Dictionary<string, string>
        {
            ["NewTerm.TermId"] = springId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["NewTerm.StudySemester"] = "8",
            ["NewTerm.Labs"] = "2",
        });
        Assert.Contains("Выберите хотя бы одну группу", noGroups, StringComparison.Ordinal);

        // Работы: название, свой дедлайн, проверка ИИ выключена.
        var lab1 = WorkId().Match(termPage).Groups[1].Value;
        var works = await PostTextAsync(client, $"/Subjects/Term?handler=Works&id={stId}", new Dictionary<string, string>
        {
            ["works[0].Id"] = lab1,
            ["works[0].Title"] = "Моделирование бизнес-процессов",
            ["works[0].Deadline"] = $"{_year}-11-30",
        });
        Assert.Contains("Работы сохранены", works, StringComparison.Ordinal);
        Assert.Contains("value=\"Моделирование бизнес-процессов\"", works, StringComparison.Ordinal);
        Assert.Contains("свой дедлайн", works, StringComparison.Ordinal);

        var added = await PostTextAsync(client, $"/Subjects/Term?handler=AddLab&id={stId}", []);
        Assert.Contains("Добавлена Лаб №4", added, StringComparison.Ordinal);
        var removed = await PostTextAsync(client, $"/Subjects/Term?handler=RemoveLab&id={stId}", []);
        Assert.Contains("Лаб №4 удалена", removed, StringComparison.Ordinal);

        // Путь: свой, затем снова по шаблону; группы: вторая добавлена.
        var custom = await PostTextAsync(client, $"/Subjects/Term?handler=Main&id={stId}", new Dictionary<string, string>
        {
            ["studySemester"] = "7",
            ["diskRootPath"] = @"Мой Диск\КорпИС\{год}",
        });
        Assert.Contains("фигурных скобках", custom, StringComparison.Ordinal);
        var suggested = await PostTextAsync(client, $"/Subjects/Term?handler=Main&id={stId}", new Dictionary<string, string>
        {
            ["studySemester"] = "8",
            ["diskRootPath"] = "что угодно",
            ["useSuggested"] = "true",
        });
        Assert.Contains($"value=\"30 Политех/КорпИС/{_year}-{_year + 1} КорпИС ИСТ - 8 сем\"", suggested, StringComparison.Ordinal);

        var groups = await PostTextAsync(client, $"/Subjects/Term?handler=Groups&id={stId}",
        [
            new("groupIds", seed.Ist41.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("groupIds", seed.Ist42.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ]);
        Assert.Contains("Группы: ИСТ-41, ИСТ-42.", groups, StringComparison.Ordinal);

        // Журнал группы: студенты × работы, пустые ячейки, тема курсовой.
        var journal = await client.GetStringAsync(new Uri($"/Journal?st={stId}&group={seed.Ist41}", UriKind.Relative));
        Assert.Contains("Белова А.С.", journal, StringComparison.Ordinal);
        Assert.Contains("Морозов И.П.", journal, StringComparison.Ordinal);
        Assert.DoesNotContain("Кузнецов", journal, StringComparison.Ordinal);
        Assert.Contains("Лаб 3", journal, StringComparison.Ordinal);
        Assert.Contains("до 30.11", journal, StringComparison.Ordinal);
        Assert.Contains("Тема курсовой", journal, StringComparison.Ordinal);
        Assert.Contains("Экзамен", journal, StringComparison.Ordinal);
        Assert.Contains("«5»", journal, StringComparison.Ordinal);

        var studentId = TopicStudent().Match(journal).Groups[1].Value;
        var topic = await PostTextAsync(client, $"/Journal?handler=Topic&st={stId}&student={studentId}", new Dictionary<string, string> { ["topic"] = "  Учёт заявок сервисного центра " });
        Assert.Contains(">Учёт заявок сервисного центра</button>", topic, StringComparison.Ordinal);
        Assert.DoesNotContain("<html", topic, StringComparison.Ordinal);

        var tooLong = await PostTextAsync(client, $"/Journal?handler=Topic&st={stId}&student={studentId}", new Dictionary<string, string> { ["topic"] = new string('т', 301) });
        Assert.Contains("не длиннее 300 символов", tooLong, StringComparison.Ordinal);

        var second = await client.GetStringAsync(new Uri($"/Journal?st={stId}&group={seed.Ist42}", UriKind.Relative));
        Assert.Contains("Кузнецов О.О.", second, StringComparison.Ordinal);
        Assert.DoesNotContain("Морозов", second, StringComparison.Ordinal);

        // Другой преподаватель: ни страницы семестра, ни журнала, ни правки темы.
        var colleague = await factory.CreateTeacherAsync(Password);
        using var other = factory.CreateClient();
        await other.LoginAsync(colleague.UserName!, Password);
        var otherTerm = await other.GetAsync(new Uri($"/Subjects/Term?id={stId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, otherTerm.StatusCode);
        var otherJournal = await other.GetStringAsync(new Uri($"/Journal?st={stId}", UriKind.Relative));
        Assert.Contains("Журнал пока пуст", otherJournal, StringComparison.Ordinal);
        Assert.DoesNotContain("Морозов", otherJournal, StringComparison.Ordinal);
        var otherTopic = await PostAsync(other, $"/Journal?handler=Topic&st={stId}&student={studentId}", new Dictionary<string, string> { ["topic"] = "Чужая тема" });
        Assert.Equal(HttpStatusCode.NotFound, otherTopic.StatusCode);
        var otherWorks = await PostAsync(other, $"/Subjects/Term?handler=AddLab&id={stId}", []);
        Assert.Equal(HttpStatusCode.NotFound, otherWorks.StatusCode);
    }

    [Fact]
    public async Task Topics_ImportedFromGroupList_OnlyForSubjectGroups()
    {
        var database = await postgres.CreateEmptyDatabaseAsync();
        await using var factory = new LabScanerWebFactory(postgres, connectionString: database);
        var teacher = await factory.CreateTeacherAsync(Password);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var import = scope.ServiceProvider.GetRequiredService<GroupImportService>();
            await using var file = File.OpenRead(_fixture);
            var preview = await import.PreviewAsync(file, "groups.xlsx");
            await import.ApplyAsync(preview.Payload, ["ИСТ-41", "ИСТ-42"]);
        }

        int termId, ist41;
        await using (var db = postgres.CreateDbContext(database))
        {
            var term = new Term(_year, TermSeason.Autumn, new DateOnly(_year, 12, 21), new DateOnly(_year + 1, 1, 11));
            db.Terms.Add(term);
            await db.SaveChangesAsync();
            termId = term.Id;
            ist41 = (await db.Groups.SingleAsync(g => g.Name == "ИСТ-41")).Id;
        }

        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, Password);
        var subjectId = await CreateSubjectAsync(client);
        var (stId, _) = await AddTermAsync(client, subjectId, new Seed(termId, ist41, 0), labs: 2, coursework: true, ist41);

        var result = await UploadTopicsAsync(client, stId);
        Assert.Contains("Темы загружены:", result, StringComparison.Ordinal);
        Assert.Contains("Пропущены листы (группа не выбрана для предмета): ИСТ42", result, StringComparison.Ordinal);

        var again = await UploadTopicsAsync(client, stId);
        Assert.Contains("Темы загружены: 0, без изменений", again, StringComparison.Ordinal);

        var journal = await client.GetStringAsync(new Uri($"/Journal?st={stId}&group={ist41}", UriKind.Relative));
        Assert.Contains("Морской порт", journal, StringComparison.Ordinal);
        Assert.Contains("Автосервис", journal, StringComparison.Ordinal);
        Assert.DoesNotContain("Ремонт телефонов", journal, StringComparison.Ordinal);
    }

    private static async Task<string> UploadTopicsAsync(HttpClient client, string stId)
    {
        var page = await client.GetStringAsync(new Uri($"/Subjects/Term?id={stId}", UriKind.Relative));
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(AuthClient.AntiforgeryToken(page)), "__RequestVerificationToken");
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(_fixture));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "groups.xlsx");
        var response = await client.PostAsync(new Uri($"/Subjects/Term?handler=Topics&id={stId}", UriKind.Relative), form);
        return await response.Content.ReadAsStringAsync();
    }

    [GeneratedRegex("href=\"\\?id=(\\d+)\"")]
    private static partial Regex SubjectId();

    [GeneratedRegex("/Subjects/Term\\?id=(\\d+)")]
    private static partial Regex TermPageId();

    [GeneratedRegex("name=\"works\\[0\\]\\.Id\" value=\"(\\d+)\"")]
    private static partial Regex WorkId();

    [GeneratedRegex("id=\"topic-(\\d+)\"")]
    private static partial Regex TopicStudent();
}
