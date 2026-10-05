using System.Text.RegularExpressions;
using LabScaner.Core.Abstractions;
using LabScaner.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace LabScaner.Integration.Tests.Infrastructure;

internal static partial class AuthClient
{
    /// <summary>Вход через форму: GET страницы → токен антифорджери → POST логина и пароля.</summary>
    public static async Task<HttpResponseMessage> LoginAsync(this HttpClient client, string login, string password)
    {
        var page = await client.GetStringAsync(new Uri("/Account/Login", UriKind.Relative));
        return await client.PostAsync(new Uri("/Account/Login", UriKind.Relative), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Login"] = login,
            ["Input.Password"] = password,
            ["__RequestVerificationToken"] = AntiforgeryToken(page),
        }));
    }

    public static string AntiforgeryToken(string html) =>
        TokenPattern().Match(html) is { Success: true } match
            ? match.Groups[1].Value
            : throw new InvalidOperationException("На странице нет токена антифорджери.");

    /// <summary>Создаёт пользователя с ролью «Преподаватель» (и, если нужно, «Администратор»).</summary>
    public static async Task<AppUser> CreateTeacherAsync(this LabScanerWebFactory factory, string password, bool admin = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = $"teacher{Guid.NewGuid():N}"[..20], DisplayName = "Преподаватель Тестовый" };
        Check(await users.CreateAsync(user, password));
        Check(await users.AddToRoleAsync(user, Roles.Teacher));
        if (admin)
        {
            Check(await users.AddToRoleAsync(user, Roles.Admin));
        }

        return user;
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
