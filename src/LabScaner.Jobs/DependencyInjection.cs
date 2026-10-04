using Microsoft.Extensions.DependencyInjection;

namespace LabScaner.Jobs;

public static class DependencyInjection
{
    /// <summary>Регистрирует задачи конвейера обработки писем. Задачи появятся на этапе 4.</summary>
    public static IServiceCollection AddJobs(this IServiceCollection services) => services;
}
