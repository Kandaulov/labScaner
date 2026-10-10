using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LabScaner.Core.Abstractions;
using LabScaner.Core.Connections;
using Microsoft.Extensions.Options;

namespace LabScaner.Infrastructure.Disk;

/// <summary>
/// REST API Яндекс Диска. Только GET и создание папок (PUT): методов удаления, перемещения и перезаписи нет —
/// система ничего не трогает на Диске преподавателя (ADR-011, проверяется тестом).
/// </summary>
public sealed class YandexDiskClient(HttpClient http, IOptions<YandexOptions> options) : IYandexDisk
{
    private readonly Uri _api = options.Value.DiskApiBaseUrl;

    public async Task<DiskInfo> GetInfoAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "v1/disk/", accessToken, cancellationToken);
        using var json = await ReadAsync(response, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK || json is null)
        {
            throw Error(response, json);
        }

        var root = json.RootElement;
        var user = root.TryGetProperty("user", out var u) ? u : default;
        var login = user.ValueKind == JsonValueKind.Object && user.TryGetProperty("login", out var l) ? l.GetString() ?? string.Empty : string.Empty;
        var name = user.ValueKind == JsonValueKind.Object && user.TryGetProperty("display_name", out var d) ? d.GetString() : null;
        return new DiskInfo(login, name, Long(root, "total_space"), Long(root, "used_space"));
    }

    public async Task<DiskFolderState> GetFolderStateAsync(string accessToken, string path, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"v1/disk/resources?path={Escape(path)}&fields=type", accessToken, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return DiskFolderState.Missing;
        }

        using var json = await ReadAsync(response, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK || json is null)
        {
            throw Error(response, json);
        }

        return json.RootElement.TryGetProperty("type", out var type) && type.GetString() == "dir" ? DiskFolderState.Exists : DiskFolderState.NotAFolder;
    }

    public async Task CreateFolderAsync(string accessToken, string path, CancellationToken cancellationToken = default)
    {
        var segments = Segments(path);
        if (segments.Count == 0)
        {
            throw new DiskException("Пустой путь на Диске.");
        }

        // Сверху вниз: существующие папки пропускаются, недостающие создаются.
        for (var i = 1; i <= segments.Count; i++)
        {
            var prefix = string.Join('/', segments.Take(i));
            using var response = await SendAsync(HttpMethod.Put, $"v1/disk/resources?path={Escape(prefix)}", accessToken, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                continue;
            }

            using var json = await ReadAsync(response, cancellationToken);
            var error = json?.RootElement.TryGetProperty("error", out var e) == true ? e.GetString() : null;
            if (response.StatusCode == HttpStatusCode.Conflict && error == "DiskPathPointsToExistentDirectoryError")
            {
                continue;
            }

            throw response.StatusCode == HttpStatusCode.Conflict
                ? new DiskException($"Не удалось создать папку «{prefix}»: по этому пути уже есть файл или нет родительской папки.")
                : Error(response, json);
        }
    }

    private static List<string> Segments(string path) =>
        [.. (path ?? string.Empty).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s != "disk:")];

    private static string Escape(string path) => Uri.EscapeDataString("disk:/" + string.Join('/', Segments(path)));

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relative, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_api, relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            return await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new DiskException("Яндекс Диск не отвечает — попробуйте позже.", inner: ex);
        }
    }

    private static async Task<JsonDocument?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return body.Length == 0 ? null : JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DiskException Error(HttpResponseMessage response, JsonDocument? json)
    {
        var description = json?.RootElement.TryGetProperty("description", out var d) == true ? d.GetString() : null;
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new DiskException("Доступ к Диску отозван или токен устарел — подключите Диск заново.", unauthorized: true),
            HttpStatusCode.InsufficientStorage => new DiskException("На Диске закончилось место."),
            HttpStatusCode.TooManyRequests => new DiskException("Яндекс ограничил частоту запросов — повторите через минуту."),
            _ => new DiskException($"Яндекс Диск вернул ошибку {(int)response.StatusCode}{(description is null ? "" : $": {description}")}."),
        };
    }

    private static long Long(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
}
