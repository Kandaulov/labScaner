using System.Net;
using System.Text.RegularExpressions;
using LabScaner.Core.Abstractions;
using LabScaner.Infrastructure;
using LabScaner.Infrastructure.Identity;
using LabScaner.Infrastructure.Persistence;
using LabScaner.Integration.Tests.Infrastructure;
using LabScaner.Web.Pages.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabScaner.Integration.Tests.Admin;

[Collection(PostgresTests.Name)]
public sealed partial class UserAdministrationTests(PostgresFixture postgres)
{
    private const string AdminPassword = "admin-password-12";

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string page, string handler, Dictionary<string, string> form)
    {
        var html = await client.GetStringAsync(new Uri(page, UriKind.Relative));
        form["__RequestVerificationToken"] = AuthClient.AntiforgeryToken(html);
        var url = handler.Contains('&', StringComparison.Ordinal) || handler.Length == 0 ? $"{page}?handler={handler}" : $"{page}?handler={handler}";
        return await client.PostAsync(new Uri(url, UriKind.Relative), new FormUrlEncodedContent(form));
    }

    private static async Task<HttpClient> AdminClientAsync(LabScanerWebFactory factory)
    {
        var admin = await factory.CreateTeacherAsync(AdminPassword, admin: true);
        var client = factory.CreateClient();
        await client.LoginAsync(admin.UserName!, AdminPassword);
        return client;
    }

    private static string TemporaryPassword(string html) =>
        TempPassword().Match(html) is { Success: true } m ? m.Groups[1].Value : throw new InvalidOperationException("Нет временного пароля на странице.");

    [Fact]
    public async Task NewTeacher_GetsTemporaryPassword_AndMustChangeItOnFirstLogin()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        using var admin = await AdminClientAsync(factory);
        var login = $"t{Guid.NewGuid():N}"[..12];

        var created = await PostFormAsync(admin, "/Admin/Users", "Create", new()
        {
            ["NewUser.Login"] = login,
            ["NewUser.DisplayName"] = "Петров П. П.",
        });
        var html = await created.Content.ReadAsStringAsync();
        Assert.Contains($"Добавлен пользователь {login}", html, StringComparison.Ordinal);
        var temporary = TemporaryPassword(html);

        using var teacher = factory.CreateClient();
        var afterLogin = await teacher.LoginAsync(login, temporary);
        Assert.EndsWith("/Settings/Password", afterLogin.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);

        // Пока пароль не сменён, любые страницы ведут на смену пароля.
        var journal = await teacher.GetAsync(new Uri("/Journal", UriKind.Relative));
        Assert.EndsWith("/Settings/Password", journal.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);

        var changed = await PostFormAsync(teacher, "/Settings/Password", "", new()
        {
            ["Current"] = temporary,
            ["New"] = "my-own-password-1",
            ["Repeat"] = "my-own-password-1",
        });
        Assert.Contains("Пароль изменён", await changed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var home = await teacher.GetAsync(new Uri("/Journal", UriKind.Relative));
        Assert.EndsWith("/Journal", home.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentOrMismatch_ShowsError()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync("teacher-password-1");
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, "teacher-password-1");

        var mismatch = await PostFormAsync(client, "/Settings/Password", "", new()
        {
            ["Current"] = "teacher-password-1",
            ["New"] = "new-password-111",
            ["Repeat"] = "new-password-222",
        });
        Assert.Contains("не совпадают", await mismatch.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var wrong = await PostFormAsync(client, "/Settings/Password", "", new()
        {
            ["Current"] = "not-my-password",
            ["New"] = "new-password-111",
            ["Repeat"] = "new-password-111",
        });
        Assert.Contains("Неверный пароль", await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResetPassword_OldPasswordStopsWorking()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        using var admin = await AdminClientAsync(factory);
        var teacher = await factory.CreateTeacherAsync("teacher-password-1");

        var reset = await PostFormAsync(admin, "/Admin/Users", $"Reset&id={teacher.Id}", []);
        var temporary = TemporaryPassword(await reset.Content.ReadAsStringAsync());

        using var old = factory.CreateClient();
        var oldLogin = await old.LoginAsync(teacher.UserName!, "teacher-password-1");
        Assert.Contains(LoginModel.WrongCredentials, await oldLogin.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var fresh = factory.CreateClient();
        var freshLogin = await fresh.LoginAsync(teacher.UserName!, temporary);
        Assert.EndsWith("/Settings/Password", freshLogin.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlockedTeacher_CannotLogIn_UntilUnblocked()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        using var admin = await AdminClientAsync(factory);
        var teacher = await factory.CreateTeacherAsync("teacher-password-1");

        await PostFormAsync(admin, "/Admin/Users", $"Block&id={teacher.Id}&blocked=true", []);
        using (var client = factory.CreateClient())
        {
            var login = await client.LoginAsync(teacher.UserName!, "teacher-password-1");
            Assert.Contains(LoginModel.Blocked, await login.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        await PostFormAsync(admin, "/Admin/Users", $"Block&id={teacher.Id}&blocked=false", []);
        using (var client = factory.CreateClient())
        {
            var login = await client.LoginAsync(teacher.UserName!, "teacher-password-1");
            Assert.DoesNotContain(LoginModel.Blocked, await login.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/Settings", UriKind.Relative))).StatusCode);
        }
    }

    [Fact]
    public async Task Admin_CannotBlockSelf()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var me = await factory.CreateTeacherAsync(AdminPassword, admin: true);
        using var client = factory.CreateClient();
        await client.LoginAsync(me.UserName!, AdminPassword);

        var response = await PostFormAsync(client, "/Admin/Users", $"Block&id={me.Id}&blocked=true", []);

        Assert.Contains("Нельзя заблокировать самого себя", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Teacher_CannotOpenUserAdministration()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        var teacher = await factory.CreateTeacherAsync("teacher-password-1");
        using var client = factory.CreateClient();
        await client.LoginAsync(teacher.UserName!, "teacher-password-1");

        var html = await client.GetStringAsync(new Uri("/Admin/Users", UriKind.Relative));

        Assert.Contains("Раздел только для администратора", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LastActiveAdmin_CannotBeBlocked()
    {
        // Отдельная пустая база: в общей тестовой базе администраторов много.
        var connection = await postgres.CreateEmptyDatabaseAsync();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection })
                .Build());
        services.AddInfrastructure(services.BuildServiceProvider().GetRequiredService<IConfiguration>());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<LabScanerDbContext>().Database.MigrateAsync();
        var roles = sp.GetRequiredService<RoleManager<IdentityRole<int>>>();
        await roles.CreateAsync(new IdentityRole<int>(Roles.Teacher));
        await roles.CreateAsync(new IdentityRole<int>(Roles.Admin));
        var admin = sp.GetRequiredService<UserAdministration>();

        var first = await admin.CreateTeacherAsync("first", "Первый", isAdmin: true);
        var second = await admin.CreateTeacherAsync("second", "Второй", isAdmin: true);
        var users = sp.GetRequiredService<UserManager<AppUser>>();
        var firstId = (await users.FindByNameAsync("first"))!.Id;
        var secondId = (await users.FindByNameAsync("second"))!.Id;
        Assert.True(first.Succeeded && second.Succeeded);

        Assert.True((await admin.SetBlockedAsync(secondId, blocked: true, actingUserId: firstId)).Succeeded);
        var last = await admin.SetBlockedAsync(firstId, blocked: true, actingUserId: secondId);

        Assert.False(last.Succeeded);
        Assert.Contains("последний администратор", last.Error, StringComparison.Ordinal);
    }

    [GeneratedRegex("class=\"secret__value\">([^<]+)<")]
    private static partial Regex TempPassword();
}
