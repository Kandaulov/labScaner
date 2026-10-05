using LabScaner.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LabScaner.Infrastructure.Persistence;

/// <summary>
/// Контекст для <c>dotnet ef migrations add</c>: миграции создаются без запуска веб-приложения.
/// Подключение к БД для генерации миграции не нужно.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LabScanerDbContext>
{
    public LabScanerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("LABSCANER_DESIGN_DB")
            ?? "Host=localhost;Database=labscaner;Username=labscaner";
        var options = new DbContextOptionsBuilder<LabScanerDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new LabScanerDbContext(options, NoCurrentTeacher.Instance);
    }
}
