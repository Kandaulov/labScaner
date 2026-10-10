using LabScaner.Core.Abstractions;
using LabScaner.Infrastructure.Identity;
using LabScaner.Infrastructure.Persistence;
using LabScaner.Infrastructure.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LabScaner.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Регистрирует реализации портов домена: БД, пользователи, почта, Диск, LLM, извлечение текста.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Не задана строка подключения к БД ConnectionStrings:Default " +
                "(переменная окружения ConnectionStrings__Default или dotnet user-secrets).");
        }

        services.AddDbContext<LabScanerDbContext>(options => options.UseNpgsql(connectionString));
        services.AddHealthChecks().AddDbContextCheck<LabScanerDbContext>("database");

        // Веб-приложение подменяет на текущего пользователя; по умолчанию преподаватель не определён.
        services.TryAddScoped<ICurrentTeacher>(_ => NoCurrentTeacher.Instance);

        services.AddIdentity<AppUser, IdentityRole<int>>(options =>
            {
                options.User.RequireUniqueEmail = false;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<LabScanerDbContext>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<RussianIdentityErrorDescriber>()
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();

        services.AddScoped<UserAdministration>();
        services.AddScoped<Import.GroupImportService>();
        services.AddScoped<Import.TopicImportService>();
        services.AddScoped<Import.TaskImportService>();
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ISecretProtector, Security.DataProtectionSecretProtector>();
        services.TryAddSingleton<IMailProbe, Mail.MailKitProbe>();

        services.Configure<Disk.YandexOptions>(configuration.GetSection(Disk.YandexOptions.Section));
        services.AddHttpClient<IYandexOAuth, Disk.YandexOAuth>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<IYandexDisk, Disk.YandexDiskClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<Disk.DiskConnectionService>();

        return services;
    }
}
