using LabScaner.Core.Abstractions;
using LabScaner.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LabScaner.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Регистрирует реализации портов домена: БД, почта, Диск, LLM, извлечение текста.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
