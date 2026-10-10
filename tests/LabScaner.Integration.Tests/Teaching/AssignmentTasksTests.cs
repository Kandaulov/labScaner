using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using LabScaner.Core.Directory;
using LabScaner.Core.Teaching;
using LabScaner.Infrastructure.Import;
using LabScaner.Integration.Tests.Infrastructure;
using Group = LabScaner.Core.Directory.Group;

namespace LabScaner.Integration.Tests.Teaching;

/// <summary>Шаг 3.5: задания из DOCX — деление, предпросмотр, применение, правка, курсовая, копирование.</summary>
[Collection(PostgresTests.Name)]
public sealed partial class AssignmentTasksTests(PostgresFixture postgres)
{
    private const string Password = "teacher-password-1";
    private static readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures", "assignments");
    private static readonly int _year = DateTime.UtcNow.Year + 1;

    private static string Fixture(string name) => Path.Combine(_fixtures, name);

    private static IReadOnlyList<DocParagraph> Read(string name)
    {
        using var file = File.OpenRead(Fixture(name));
        return DocxReader.Read(file);
    }

    [Fact]
    public void Fixture7Sem_SplitsIntoFourLabs_WithTitlesFromGoal()
    {
        var split = TaskDocumentSplitter.Split(Read("korpis-7sem-labs.docx"));

        Assert.Equal([1, 2, 3, 4], split.Labs.Select(l => l.Number));
        Assert.Equal("Создание сервиса загрузки транспортных файлов", split.Labs[1].Title);
        Assert.Equal("Изучение механизмов работы с RabbitMQ", split.Labs[2].Title);
        Assert.Contains("Требования по оформлению и сдаче работ", split.Preamble, StringComparison.Ordinal);
        Assert.DoesNotContain("Лабораторная работа", split.Preamble, StringComparison.Ordinal);
        Assert.Contains("- Проверки наличия новых файлов", split.Labs[3].Text, StringComparison.Ordinal);
        Assert.Contains("  - Дата и время проверки, результат проверки", split.Labs[3].Text, StringComparison.Ordinal);
        Assert.Contains("Проверки наличия новых файлов: Дата и время проверки, результат проверки", split.Labs[3].Checklist);
        Assert.Contains(split.Labs[3].Checklist, c => c.StartsWith("В отчёте: Код сервера", StringComparison.Ordinal));

        // Лаб №3 — заготовка: система предупреждает.
        Assert.Contains(split.Warnings, w => w.Contains("№3 короткое", StringComparison.Ordinal));
    }

    [Fact]
    public void Fixture6Sem_SplitsIntoThreeLabs_FirstWithoutTitle()
    {
        var split = TaskDocumentSplitter.Split(Read("korpis-6sem-labs.docx"));

        Assert.Equal([1, 2, 3], split.Labs.Select(l => l.Number));
        Assert.Equal(string.Empty, split.Labs[0].Title);
        Assert.Contains("IDEF0", split.Labs[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("Разработать домен для предметной области", split.Labs[1].Title, StringComparison.Ordinal);
        Assert.Contains("Валидация входных данных", split.Labs[2].Checklist);
        Assert.Single(split.Warnings, w => w.Contains("№1 нет названия", StringComparison.Ordinal));
        Assert.Contains("3 курс, 6 семестр", split.Preamble, StringComparison.Ordinal);
    }

    [Fact]
    public void FixtureCoursework_WholeText_ChecklistFromCriteria()
    {
        var task = TaskDocumentSplitter.ParseCoursework(Read("korpis-coursework.docx"));

        Assert.Contains("Проектирование вычислительных сетей", task.Text, StringComparison.Ordinal);
        Assert.Contains("Примеры тем курсовой", task.Text, StringComparison.Ordinal);
        Assert.Equal(9, task.Checklist.Count);
        Assert.Equal("Соответствие ПЗ требуемой структуре", task.Checklist[0]);
        Assert.Equal("Сдача работы в срок", task.Checklist[^1]);
        Assert.Empty(task.Warnings);
    }

    [Fact]
    public void Fixtures_ContainNoRealContacts()
    {
        foreach (var file in System.IO.Directory.GetFiles(_fixtures, "*.docx"))
        {
            var text = string.Join("\n", Read(Path.GetFileName(file)).Select(p => p.Text));
            Assert.DoesNotContain("ulstu", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DocxReader_NotADocx_ThrowsReadableError()
    {
        using var file = new MemoryStream("Лабораторная работа №1"u8.ToArray());

        var error = Assert.Throws<InvalidDataException>(() => DocxReader.Read(file));

        Assert.Contains(".docx", error.Message, StringComparison.Ordinal);
    }

    private sealed record Seed(int Term, int NextTerm, int Group);

    private static async Task<Seed> SeedAsync(PostgresFixture postgres, string database)
    {
        await using var db = postgres.CreateDbContext(database);
        var term = new Term(_year, TermSeason.Autumn, new DateOnly(_year, 12, 21), new DateOnly(_year + 1, 1, 11));
        var next = new Term(_year + 1, TermSeason.Autumn, new DateOnly(_year + 1, 12, 20), new DateOnly(_year + 2, 1, 10));
        var group = new Group("ИСТ-41");
        group.AddStudent(PersonName.Parse("Морозов Иван Петрович"));
        db.Terms.AddRange(term, next);
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return new Seed(term.Id, next.Id, group.Id);
    }

    private static string Inv(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string tokenPage, string url, IEnumerable<KeyValuePair<string, string>> form)
    {
        var page = await client.GetStringAsync(new Uri(tokenPage, UriKind.Relative));
        var fields = form.Append(new KeyValuePair<string, string>("__RequestVerificationToken", AuthClient.AntiforgeryToken(page)));
        return await client.PostAsync(new Uri(url, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private static async Task<string> UploadAsync(HttpClient client, string stId, string handler, string fixture, string fileName = "задания.docx")
    {
        var page = await client.GetStringAsync(new Uri($"/Subjects/Term?id={stId}", UriKind.Relative));
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(AuthClient.AntiforgeryToken(page)), "__RequestVerificationToken");
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(Fixture(fixture)));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        form.Add(file, "file", fileName);
        var response = await client.PostAsync(new Uri($"/Subjects/Term?handler={handler}&id={stId}", UriKind.Relative), form);
        return await response.Content.ReadAsStringAsync() + "\n<!-- url: " + response.RequestMessage!.RequestUri + " -->";
    }

    /// <summary>Предмет КорпИС и предмет в семестре с 8 лабами и курсовой.</summary>
    private static async Task<(string SubjectId, string StId)> CreateSubjectTermAsync(HttpClient client, int termId, int groupId, int labs = 8)
    {
        var subjects = await PostAsync(client, "/Subjects", "/Subjects?handler=Save", new Dictionary<string, string>
        {
            ["Input.Name"] = "Корпоративные информационные системы",
            ["Input.Code"] = "КорпИС",
            ["Input.FinalAssessment"] = "Exam",
            ["Input.DiskPathTemplate"] = "{Код}/{Учебный год} {Код} {Направление} - {N} сем",
        });
        var subjectId = SubjectIdPattern().Match(await subjects.Content.ReadAsStringAsync()).Groups[1].Value;
        return (subjectId, await AddTermAsync(client, subjectId, termId, groupId, labs));
    }

    private static async Task<string> AddTermAsync(HttpClient client, string subjectId, int termId, int groupId, int labs)
    {
        var response = await PostAsync(client, "/Subjects", $"/Subjects?handler=AddTerm&id={subjectId}", new Dictionary<string, string>
        {
            ["NewTerm.TermId"] = Inv(termId),
            ["NewTerm.StudySemester"] = "7",
            ["NewTerm.Labs"] = Inv(labs),
            ["NewTerm.Coursework"] = "true",
            ["NewTerm.GroupIds"] = Inv(groupId),
        });
        return TermPageId().Match(response.RequestMessage!.RequestUri!.ToString()).Groups[1].Value;
    }

    [Fact]
    public async Task Teacher_UploadsSplitsAppliesAndEditsTasks_CopiesToNextYear_OthersCannot()
    {
        var database = await postgres.CreateEmptyDatabaseAsync();
        await using var factory = new LabScanerWebFactory(postgres, connectionString: database);
        var teacher = await factory.CreateTeacherAsync(Password);
        var seed = await SeedAsync(postgres, database);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, Password);
        var (subjectId, stId) = await CreateSubjectTermAsync(client, seed.Term, seed.Group);
        Assert.NotEmpty(stId);

        // Не DOCX — понятная ошибка, ничего не сохраняется.
        var wrongType = await UploadAsync(client, stId, "UploadLabs", "korpis-7sem-labs.docx", "задания.doc");
        Assert.Contains("Нужен документ Word в формате .docx", wrongType, StringComparison.Ordinal);

        // Загрузка → предпросмотр.
        var preview = await UploadAsync(client, stId, "UploadLabs", "korpis-7sem-labs.docx", "04_2026 КорпИС 7 сем.docx");
        Assert.Contains("Найдено лабораторных: 4", preview, StringComparison.Ordinal);
        Assert.Contains("value=\"Изучение механизмов работы с RabbitMQ\"", preview, StringComparison.Ordinal);
        Assert.Contains("№3 короткое", preview, StringComparison.Ordinal);
        Assert.Contains("Удалить Лаб №5, Лаб №6, Лаб №7, Лаб №8", preview, StringComparison.Ordinal);
        Assert.Contains("04_2026 КорпИС 7 сем.docx", preview, StringComparison.Ordinal);
        var docId = DocPattern().Match(preview).Groups[1].Value;
        Assert.NotEmpty(docId);

        // До подтверждения работы не меняются.
        var untouched = await client.GetStringAsync(new Uri($"/Subjects/Term?id={stId}", UriKind.Relative));
        Assert.Contains("Лаб №8", untouched, StringComparison.Ordinal);
        Assert.DoesNotContain("RabbitMQ", untouched, StringComparison.Ordinal);

        // Применение: своё название первой лабы, лишние лабы удаляются, общие требования берутся из документа.
        var applied = await PostAsync(client, $"/Subjects/Tasks?id={stId}&doc={docId}", $"/Subjects/Tasks?handler=Apply&id={stId}&doc={docId}", new Dictionary<string, string>
        {
            ["titles[1]"] = "ETL-конвейер",
            ["titles[2]"] = "Сервис загрузки",
            ["removeMissing"] = "true",
            ["replaceRequirements"] = "true",
        });
        var termPage = await applied.Content.ReadAsStringAsync();
        Assert.Contains("Задания применены: лабораторных — 4", termPage, StringComparison.Ordinal);
        Assert.Contains("value=\"ETL-конвейер\"", termPage, StringComparison.Ordinal);
        Assert.Contains("value=\"Изучение механизмов работы с RabbitMQ\"", termPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Лаб №5", termPage, StringComparison.Ordinal);
        Assert.Contains("Применён «04_2026 КорпИС 7 сем.docx»", termPage, StringComparison.Ordinal);
        Assert.Contains("Требования по оформлению и сдаче работ", termPage, StringComparison.Ordinal);
        Assert.Contains("нет задания", termPage, StringComparison.Ordinal); // курсовая без задания

        // Задание лабораторной: правка текста и чек-листа.
        var labIds = AssignmentLink().Matches(termPage).Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Equal(5, labIds.Count); // 4 лабы + курсовая
        var lab3 = await client.GetStringAsync(new Uri($"/Subjects/Assignment?id={labIds[2]}", UriKind.Relative));
        Assert.Contains("Поднять RMQ", lab3, StringComparison.Ordinal);
        Assert.Contains("Код сервиса, описание сервиса", lab3, StringComparison.Ordinal);

        var saved = await PostAsync(client, $"/Subjects/Assignment?id={labIds[2]}", $"/Subjects/Assignment?id={labIds[2]}", new Dictionary<string, string>
        {
            ["title"] = "Очереди RabbitMQ",
            ["taskText"] = "Поднять RabbitMQ, создать очередь, отправлять в неё события конвейера.",
            ["checklist"] = "RabbitMQ развёрнут\r\n\r\n- Создана очередь проекта\r\nКонвейер пишет события в очередь",
            ["ai"] = "true",
        });
        var savedPage = WebUtility.HtmlDecode(await saved.Content.ReadAsStringAsync()); // переводы строк в textarea кодируются
        Assert.Contains("Сохранено.", savedPage, StringComparison.Ordinal);
        Assert.Contains("RabbitMQ развёрнут\nСоздана очередь проекта\nКонвейер пишет события в очередь</textarea>", savedPage, StringComparison.Ordinal);

        var tooLong = await PostAsync(client, $"/Subjects/Assignment?id={labIds[2]}", $"/Subjects/Assignment?id={labIds[2]}", new Dictionary<string, string>
        {
            ["title"] = "Очереди RabbitMQ",
            ["checklist"] = new string('п', 301),
        });
        Assert.Contains("Пункт чек-листа длиннее 300 символов", await tooLong.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Курсовая: весь документ, чек-лист из «Критериев оценки».
        var coursework = await UploadAsync(client, stId, "UploadCoursework", "korpis-coursework.docx");
        Assert.Contains("Задание на курсовую загружено", coursework, StringComparison.Ordinal);
        Assert.Contains("пунктов чек-листа — 9", coursework, StringComparison.Ordinal);

        // Новый учебный год: копирование из прошлого семестра того же номера.
        var nextId = await AddTermAsync(client, subjectId, seed.NextTerm, seed.Group, labs: 1);
        var nextPage = await client.GetStringAsync(new Uri($"/Subjects/Term?id={nextId}", UriKind.Relative));
        Assert.Contains("Скопировать из другого семестра", nextPage, StringComparison.Ordinal);
        var copied = await PostAsync(client, $"/Subjects/Term?id={nextId}", $"/Subjects/Term?handler=Copy&id={nextId}", new Dictionary<string, string> { ["sourceId"] = stId });
        var copiedPage = WebUtility.HtmlDecode(await copied.Content.ReadAsStringAsync()); // «+» в разметке кодируется
        Assert.Contains("Скопировано из", copiedPage, StringComparison.Ordinal);
        Assert.Contains("value=\"Очереди RabbitMQ\"", copiedPage, StringComparison.Ordinal);
        Assert.Contains("лаб — 4 + курсовая", copiedPage, StringComparison.Ordinal);
        Assert.Contains("Требования по оформлению и сдаче работ", copiedPage, StringComparison.Ordinal);

        // Другой преподаватель: ни предпросмотра, ни задания, ни загрузки.
        var colleague = await factory.CreateTeacherAsync(Password);
        using var other = factory.CreateClient();
        await other.LoginAsync(colleague.UserName!, Password);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(new Uri($"/Subjects/Tasks?id={stId}&doc={docId}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(new Uri($"/Subjects/Assignment?id={labIds[2]}", UriKind.Relative))).StatusCode);
        var otherApply = await PostAsync(other, "/Subjects", $"/Subjects/Tasks?handler=Apply&id={stId}&doc={docId}", new Dictionary<string, string> { ["removeMissing"] = "true" });
        Assert.Equal(HttpStatusCode.NotFound, otherApply.StatusCode);
    }

    [GeneratedRegex("href=\"\\?id=(\\d+)\"")]
    private static partial Regex SubjectIdPattern();

    [GeneratedRegex("/Subjects/Term\\?id=(\\d+)")]
    private static partial Regex TermPageId();

    [GeneratedRegex("[?&;]doc=(\\d+)")]
    private static partial Regex DocPattern();

    [GeneratedRegex("href=\"/Subjects/Assignment\\?id=(\\d+)\"")]
    private static partial Regex AssignmentLink();
}
