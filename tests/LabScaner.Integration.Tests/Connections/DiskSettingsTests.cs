using System.Net;
using LabScaner.Core.Abstractions;
using LabScaner.Core.Connections;
using LabScaner.Core.Directory;
using LabScaner.Core.Subjects;
using LabScaner.Core.Teaching;
using LabScaner.Integration.Tests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Group = LabScaner.Core.Directory.Group;

namespace LabScaner.Integration.Tests.Connections;

/// <summary>Шаг 3.6б: подключение Яндекс Диска по коду, проверка и создание папок семестров — на подставном Яндексе.</summary>
[Collection(PostgresTests.Name)]
public sealed class DiskSettingsTests(PostgresFixture postgres)
{
    private const string TeacherPassword = "teacher-password-1";
    private static readonly int _year = DateTime.UtcNow.Year + 1;

    /// <summary>Подставной OAuth: подходит только код «good-code».</summary>
    private sealed class FakeOAuth : IYandexOAuth
    {
        public int Refreshes { get; private set; }

        public bool IsConfigured => true;

        public Uri AuthorizeUrl => new("https://oauth.test/authorize?response_type=code&client_id=x");

        public Task<DiskTokens> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default) =>
            code.Trim() == "good-code"
                ? Task.FromResult(new DiskTokens("access-token-1", "refresh-token-1", DateTimeOffset.UtcNow.AddDays(365)))
                : throw new DiskException("Код не подошёл: он неверный, уже использован или устарел (живёт 10 минут). Получите новый код.");

        public Task<DiskTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            Refreshes++;
            return Task.FromResult(new DiskTokens("access-token-2", "refresh-token-2", DateTimeOffset.UtcNow.AddDays(365)));
        }
    }

    /// <summary>Подставной Диск: множество существующих папок; удалять не умеет вовсе.</summary>
    private sealed class FakeDisk : IYandexDisk
    {
        public HashSet<string> Folders { get; } = new(StringComparer.Ordinal);

        public List<string> Tokens { get; } = [];

        private static string Norm(string path) => string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries));

        public Task<DiskInfo> GetInfoAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            Tokens.Add(accessToken);
            return Task.FromResult(new DiskInfo("v.ivanov", "Иван Иванов", 10L << 30, 3L << 30));
        }

        public Task<DiskFolderState> GetFolderStateAsync(string accessToken, string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(Folders.Contains(Norm(path)) ? DiskFolderState.Exists : DiskFolderState.Missing);

        public Task CreateFolderAsync(string accessToken, string path, CancellationToken cancellationToken = default)
        {
            var parts = Norm(path).Split('/');
            for (var i = 1; i <= parts.Length; i++)
            {
                Folders.Add(string.Join('/', parts.Take(i)));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedTeacher(int id) : ICurrentTeacher
    {
        public int? TeacherId => id;
    }

    private static async Task<string> PostAsync(HttpClient client, string url, Dictionary<string, string> form)
    {
        var page = await client.GetStringAsync(new Uri("/Settings/Disk", UriKind.Relative));
        form["__RequestVerificationToken"] = AuthClient.AntiforgeryToken(page);
        var response = await client.PostAsync(new Uri(url, UriKind.Relative), new FormUrlEncodedContent(form));
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task NotConfigured_ExplainsWhatAdminMustDo()
    {
        var database = await postgres.CreateEmptyDatabaseAsync();
        await using var factory = new LabScanerWebFactory(postgres, connectionString: database);
        var teacher = await factory.CreateTeacherAsync(TeacherPassword);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, TeacherPassword);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/Settings/Disk", UriKind.Relative)));

        Assert.Contains("Приложение Яндекса не настроено на сервере", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Открыть Яндекс", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Teacher_ConnectsDisk_ChecksAndCreatesFolders_TokenRefreshes_OthersDoNotSee()
    {
        var oauth = new FakeOAuth();
        var disk = new FakeDisk();
        var database = await postgres.CreateEmptyDatabaseAsync();
        await using var baseFactory = new LabScanerWebFactory(postgres, connectionString: database);
        await using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.AddSingleton<IYandexOAuth>(oauth);
            s.AddSingleton<IYandexDisk>(disk);
        }));
        var teacher = await baseFactory.CreateTeacherAsync(TeacherPassword);
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, TeacherPassword);

        // Предмет в семестре с группой и курсовой.
        const string root = "30 Политех/03 КорпИС/2027 КорпИС ИСТ - 7 сем";
        await using (var db = postgres.CreateDbContext(new FixedTeacher(teacher.Id), database))
        {
            var term = new Term(_year, TermSeason.Autumn, new DateOnly(_year, 12, 21), new DateOnly(_year + 1, 1, 11));
            var group = new Group("ИСТ-41");
            var subject = new Subject("Корпоративные информационные системы", "КорпИС");
            var st = new SubjectTerm(subject, term, 7, root);
            st.AddGroup(group);
            st.SetCoursework(true);
            db.AddRange(term, group, subject, st);
            await db.SaveChangesAsync();
        }

        var start = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/Settings/Disk", UriKind.Relative)));
        Assert.Contains("Подключить Яндекс Диск", start, StringComparison.Ordinal);
        Assert.Contains("https://oauth.test/authorize", start, StringComparison.Ordinal);

        var bad = await PostAsync(client, "/Settings/Disk?handler=Connect", new() { ["code"] = "wrong" });
        Assert.Contains("Код не подошёл", bad, StringComparison.Ordinal);

        var connected = await PostAsync(client, "/Settings/Disk?handler=Connect", new() { ["code"] = " good-code " });
        Assert.Contains("Диск подключён: Иван Иванов", connected, StringComparison.Ordinal);
        Assert.Contains("занято 3,0 ГБ из 10 ГБ", connected, StringComparison.Ordinal);
        Assert.Contains("папки нет — проверьте путь", connected, StringComparison.Ordinal);
        Assert.DoesNotContain("access-token-1", connected, StringComparison.Ordinal);

        await using (var db = postgres.CreateDbContext(new FixedTeacher(teacher.Id), database))
        {
            var row = await db.DiskConnections.SingleAsync();
            Assert.DoesNotContain("access-token-1", row.AccessTokenProtected, StringComparison.Ordinal);
            Assert.DoesNotContain("refresh-token-1", row.RefreshTokenProtected!, StringComparison.Ordinal);
        }

        // Корневой папки нет — создаётся только по явной кнопке, вместе с подпапками.
        var stId = System.Text.RegularExpressions.Regex.Match(connected, "[?&]st=(\\d+)").Groups[1].Value;
        Assert.NotEmpty(stId);
        var created = await PostAsync(client, $"/Settings/Disk?handler=Create&st={stId}&root=true", []);
        Assert.Contains("Папки «КорпИС", created, StringComparison.Ordinal);
        Assert.Contains("✓ папка и подпапки на месте", created, StringComparison.Ordinal);
        Assert.Contains($"{root}/ИСТ-41 - ЛР", disk.Folders);
        Assert.Contains($"{root}/ИСТ-41 - Кр", disk.Folders);
        Assert.Contains($"{root}/Задания", disk.Folders);

        // Пропавшая подпапка видна при проверке и создаётся отдельно.
        disk.Folders.Remove($"{root}/Задания");
        var check = await PostAsync(client, "/Settings/Disk?handler=Check", []);
        Assert.Contains("нет: Задания", check, StringComparison.Ordinal);
        await PostAsync(client, $"/Settings/Disk?handler=Create&st={stId}", []);
        Assert.Contains($"{root}/Задания", disk.Folders);

        // Срок токена подходит к концу — при проверке он обновляется сам.
        await using (var db = postgres.CreateDbContext(new FixedTeacher(teacher.Id), database))
        {
            var row = await db.DiskConnections.SingleAsync();
            row.SetTokens(row.AccessTokenProtected, null, DateTimeOffset.UtcNow.AddDays(3));
            await db.SaveChangesAsync();
        }

        await PostAsync(client, "/Settings/Disk?handler=Check", []);
        Assert.Equal(1, oauth.Refreshes);
        Assert.Equal("access-token-2", disk.Tokens[^1]);

        // Другой преподаватель видит только форму подключения.
        var colleague = await baseFactory.CreateTeacherAsync(TeacherPassword);
        using var other = factory.CreateClient();
        await other.LoginAsync(colleague.UserName!, TeacherPassword);
        var otherPage = WebUtility.HtmlDecode(await other.GetStringAsync(new Uri("/Settings/Disk", UriKind.Relative)));
        Assert.Contains("Подключить Яндекс Диск", otherPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Иван Иванов", otherPage, StringComparison.Ordinal);

        var disconnected = await PostAsync(client, "/Settings/Disk?handler=Disconnect", []);
        Assert.Contains("Диск отключён", disconnected, StringComparison.Ordinal);
        Assert.Contains("Подключить Яндекс Диск", disconnected, StringComparison.Ordinal);
    }
}
