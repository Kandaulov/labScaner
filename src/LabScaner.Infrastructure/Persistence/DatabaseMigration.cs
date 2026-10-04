using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LabScaner.Infrastructure.Persistence;

public static class DatabaseMigration
{
    /// <summary>
    /// Применяет миграции при старте (ARCHITECTURE.md, раздел CD). Отключается
    /// настройкой <c>Database:MigrateOnStartup=false</c>.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabScanerDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigration));

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        logger.LogInformation("Применение миграций БД: {Migrations}", string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
    }
}
