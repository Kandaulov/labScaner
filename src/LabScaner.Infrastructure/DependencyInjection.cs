using LabScaner.Core.Abstractions;
using LabScaner.Infrastructure.Persistence;
using LabScaner.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabScaner.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Регистрирует реализации портов домена: БД, почта, Диск, LLM, извлечение текста.</summary>
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

        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
