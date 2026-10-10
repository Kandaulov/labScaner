using System.Net;
using System.Text;
using LabScaner.Core.Abstractions;
using LabScaner.Core.Connections;
using LabScaner.Infrastructure.Disk;
using Microsoft.Extensions.Options;

namespace LabScaner.Integration.Tests.Connections;

/// <summary>Клиенты Яндекса на подставном HTTP: адреса, заголовки, разбор ответов и ошибок (ADR-031).</summary>
public sealed class YandexClientTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

    private static readonly IOptions<YandexOptions> _options = Options.Create(new YandexOptions
    {
        ClientId = "client-1",
        ClientSecret = "secret-1",
        OAuthBaseUrl = new Uri("https://oauth.test/"),
        DiskApiBaseUrl = new Uri("https://disk.test/"),
    });

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => _now;
    }

    /// <summary>Отвечает по очереди заготовленными ответами и запоминает запросы.</summary>
    private sealed class StubHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<(HttpMethod Method, string Url, string? Auth, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body));
            var (status, json) = responses[Math.Min(_next++, responses.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    [Fact]
    public async Task ExchangeCode_PostsCredentials_ReturnsTokensWithExpiry()
    {
        using var handler = new StubHandler((HttpStatusCode.OK, """{"access_token":"AT","refresh_token":"RT","expires_in":31536000,"token_type":"bearer"}"""));
        using var http = new HttpClient(handler);
        var oauth = new YandexOAuth(http, _options, new FixedClock());

        var tokens = await oauth.ExchangeCodeAsync(" 1234567 ");

        Assert.Equal("AT", tokens.AccessToken);
        Assert.Equal("RT", tokens.RefreshToken);
        Assert.Equal(_now.AddSeconds(31536000), tokens.ExpiresAt);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://oauth.test/token", request.Url);
        Assert.Contains("grant_type=authorization_code", request.Body, StringComparison.Ordinal);
        Assert.Contains("code=1234567", request.Body, StringComparison.Ordinal);
        Assert.Contains("client_secret=secret-1", request.Body, StringComparison.Ordinal);
        Assert.StartsWith("https://oauth.test/authorize?response_type=code&client_id=client-1", oauth.AuthorizeUrl.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExchangeCode_InvalidGrant_ReadableError_RefreshInvalid_Unauthorized()
    {
        using var handler = new StubHandler((HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Code has expired"}"""));
        using var http = new HttpClient(handler);
        var oauth = new YandexOAuth(http, _options, new FixedClock());

        var code = await Assert.ThrowsAsync<DiskException>(() => oauth.ExchangeCodeAsync("123"));
        Assert.Contains("Код не подошёл", code.Message, StringComparison.Ordinal);

        var refresh = await Assert.ThrowsAsync<DiskException>(() => oauth.RefreshAsync("RT"));
        Assert.True(refresh.Unauthorized);
        Assert.Contains("grant_type=refresh_token", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotConfigured_Throws()
    {
        using var http = new HttpClient(new StubHandler((HttpStatusCode.OK, "{}")));
        var oauth = new YandexOAuth(http, Options.Create(new YandexOptions()), new FixedClock());

        Assert.False(oauth.IsConfigured);
        await Assert.ThrowsAsync<DiskException>(() => oauth.ExchangeCodeAsync("123"));
    }

    [Fact]
    public async Task GetInfo_ParsesOwnerAndSpace_SendsOAuthHeader()
    {
        using var handler = new StubHandler((HttpStatusCode.OK, """{"total_space":10737418240,"used_space":1073741824,"user":{"login":"v.ivanov","display_name":"Иван Иванов"}}"""));
        using var http = new HttpClient(handler);
        var disk = new YandexDiskClient(http, _options);

        var info = await disk.GetInfoAsync("AT");

        Assert.Equal(new DiskInfo("v.ivanov", "Иван Иванов", 10737418240, 1073741824), info);
        Assert.Equal("https://disk.test/v1/disk/", handler.Requests[0].Url);
        Assert.Equal("OAuth AT", handler.Requests[0].Auth);
    }

    [Fact]
    public async Task Unauthorized_MarkedForReconnect()
    {
        using var http = new HttpClient(new StubHandler((HttpStatusCode.Unauthorized, """{"error":"UnauthorizedError"}""")));
        var disk = new YandexDiskClient(http, _options);

        var error = await Assert.ThrowsAsync<DiskException>(() => disk.GetInfoAsync("AT"));

        Assert.True(error.Unauthorized);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"type":"dir"}""", DiskFolderState.Exists)]
    [InlineData(HttpStatusCode.OK, """{"type":"file"}""", DiskFolderState.NotAFolder)]
    [InlineData(HttpStatusCode.NotFound, """{"error":"DiskNotFoundError"}""", DiskFolderState.Missing)]
    public async Task FolderState(HttpStatusCode status, string body, DiskFolderState expected)
    {
        using var handler = new StubHandler((status, body));
        using var http = new HttpClient(handler);
        var disk = new YandexDiskClient(http, _options);

        Assert.Equal(expected, await disk.GetFolderStateAsync("AT", "30 Политех/03 КорпИС/ИСТ-41 - ЛР"));
        Assert.Equal(
            "https://disk.test/v1/disk/resources?path=disk%3A%2F30%20%D0%9F%D0%BE%D0%BB%D0%B8%D1%82%D0%B5%D1%85%2F03%20%D0%9A%D0%BE%D1%80%D0%BF%D0%98%D0%A1%2F%D0%98%D0%A1%D0%A2-41%20-%20%D0%9B%D0%A0&fields=type",
            handler.Requests[0].Url);
    }

    [Fact]
    public async Task CreateFolder_CreatesMissingAncestors_SkipsExisting()
    {
        using var handler = new StubHandler(
            (HttpStatusCode.Conflict, """{"error":"DiskPathPointsToExistentDirectoryError"}"""),
            (HttpStatusCode.Created, """{"href":"x"}"""),
            (HttpStatusCode.Created, """{"href":"y"}"""));
        using var http = new HttpClient(handler);
        var disk = new YandexDiskClient(http, _options);

        await disk.CreateFolderAsync("AT", "/30 Политех/КорпИС/Задания/");

        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Put, r.Method));
        Assert.Equal(3, handler.Requests.Count);
        Assert.EndsWith("path=disk%3A%2F30%20%D0%9F%D0%BE%D0%BB%D0%B8%D1%82%D0%B5%D1%85", handler.Requests[0].Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateFolder_FileInTheWay_Throws()
    {
        using var http = new HttpClient(new StubHandler((HttpStatusCode.Conflict, """{"error":"DiskPathPointsToExistentFileError"}""")));
        var disk = new YandexDiskClient(http, _options);

        var error = await Assert.ThrowsAsync<DiskException>(() => disk.CreateFolderAsync("AT", "a"));

        Assert.Contains("уже есть файл", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiskPort_HasNoDestructiveOperations()
    {
        // ADR-011: система ничего не удаляет, не переименовывает и не перезаписывает на Диске преподавателя.
        var names = typeof(IYandexDisk).GetMethods().Select(m => m.Name).ToList();
        string[] forbidden = ["Delete", "Remove", "Move", "Rename", "Overwrite", "Trash", "Publish"];

        Assert.DoesNotContain(names, n => forbidden.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }
}
