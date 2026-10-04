using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabScaner.Integration.Tests.Infrastructure;

/// <summary>Приложение целиком поверх тестовой базы из <see cref="PostgresFixture"/>.</summary>
public sealed class LabScanerWebFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
    }
}
