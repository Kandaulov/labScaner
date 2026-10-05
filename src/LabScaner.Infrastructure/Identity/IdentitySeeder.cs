using LabScaner.Core.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabScaner.Infrastructure.Identity;

public static partial class IdentitySeeder
{
    /// <summary>
    /// Создаёт роли и — если пользователей ещё нет — первого администратора из настроек
    /// <c>Bootstrap:AdminLogin</c>, <c>Bootstrap:AdminPassword</c>, <c>Bootstrap:AdminDisplayName</c>.
    /// Повторный запуск ничего не меняет.
    /// </summary>
    public static async Task SeedIdentityAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<int>>>();
        var userManager = provider.GetRequiredService<UserManager<AppUser>>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));

        foreach (var role in new[] { Roles.Teacher, Roles.Admin })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole<int>(role)), $"роль {role}");
            }
        }

        if (userManager.Users.Any())
        {
            return;
        }

        var login = configuration["Bootstrap:AdminLogin"];
        var password = configuration["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            LogNoAdmin(logger);
            return;
        }

        var admin = new AppUser
        {
            UserName = login,
            DisplayName = configuration["Bootstrap:AdminDisplayName"] ?? login,
        };
        EnsureSucceeded(await userManager.CreateAsync(admin, password), "администратор");
        EnsureSucceeded(await userManager.AddToRolesAsync(admin, [Roles.Teacher, Roles.Admin]), "роли администратора");
        LogAdminCreated(logger, login);
    }

    private static void EnsureSucceeded(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Не удалось создать {what}: {errors}");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Пользователей нет, а Bootstrap:AdminLogin / Bootstrap:AdminPassword не заданы — войти в систему будет некому.")]
    private static partial void LogNoAdmin(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Создан первый администратор {Login}.")]
    private static partial void LogAdminCreated(ILogger logger, string login);
}
