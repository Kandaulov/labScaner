using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LabScaner.Integration.Tests.Infrastructure;

/// <summary>Приложение целиком поверх тестовой базы из <see cref="PostgresFixture"/>.</summary>
public sealed class LabScanerWebFactory(PostgresFixture postgres, int loginRequestsPerMinute = 1000) : WebApplicationFactory<Program>
{
    public const string AdminLogin = "admin";
    public const string AdminPassword = "admin-password-1";
    public const string AdminDisplayName = "Администратор Тестовый";

    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), "labscaner-tests-keys");

    public HttpClient CreateClientNoRedirect() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
        builder.UseSetting("Bootstrap:AdminLogin", AdminLogin);
        builder.UseSetting("Bootstrap:AdminPassword", AdminPassword);
        builder.UseSetting("Bootstrap:AdminDisplayName", AdminDisplayName);
        builder.UseSetting("DataProtection:KeysPath", _keysPath);
        builder.UseSetting("Security:LoginRequestsPerMinute", loginRequestsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
