using System.Net;
using LabScaner.Integration.Tests.Infrastructure;

namespace LabScaner.Integration.Tests;

[Collection(PostgresTests.Name)]
public sealed class SmokeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Health_IsAnonymousAndHealthy()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        using var client = factory.CreateClientNoRedirect();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LoginPage_RendersInRussian()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/Account/Login", UriKind.Relative));

        Assert.Contains("<html lang=\"ru\">", html, StringComparison.Ordinal);
        Assert.Contains("Войти", html, StringComparison.Ordinal);
    }
}
