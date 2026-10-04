using System.Net;
using LabScaner.Integration.Tests.Infrastructure;

namespace LabScaner.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SmokeTests(PostgresFixture postgres) : IAsyncLifetime
{
    private LabScanerWebFactory _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new LabScanerWebFactory(postgres);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Health_WithDatabase_ReturnsHealthy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Index_RendersRussianPage()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("<html lang=\"ru\">", html, StringComparison.Ordinal);
        Assert.Contains("labScaner", html, StringComparison.Ordinal);
    }
}
