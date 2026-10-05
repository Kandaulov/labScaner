using System.Net;
using LabScaner.Integration.Tests.Infrastructure;

namespace LabScaner.Integration.Tests;

[Collection(PostgresTests.Name)]
public sealed class SmokeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Health_WithDatabase_ReturnsHealthy()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Index_RendersRussianPage()
    {
        await using var factory = new LabScanerWebFactory(postgres);
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("<html lang=\"ru\">", html, StringComparison.Ordinal);
        Assert.Contains("labScaner", html, StringComparison.Ordinal);
    }
}
