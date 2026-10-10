using System.Net;
using System.Text.Json;
using LabScaner.Core.Abstractions;
using LabScaner.Core.Connections;
using Microsoft.Extensions.Options;

namespace LabScaner.Infrastructure.Disk;

/// <summary>
/// OAuth Яндекса по коду подтверждения: приложение типа «Для доступа к API или отладки» с адресом возврата
/// https://oauth.yandex.ru/verification_code — Яндекс показывает код, преподаватель вставляет его в labScaner.
/// Публичный адрес сервера для этого не нужен.
/// </summary>
public sealed class YandexOAuth(HttpClient http, IOptions<YandexOptions> options, IClock clock) : IYandexOAuth
{
    private readonly YandexOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ClientId) && !string.IsNullOrWhiteSpace(_options.ClientSecret);

    public Uri AuthorizeUrl => new(_options.OAuthBaseUrl, $"authorize?response_type=code&client_id={Uri.EscapeDataString(_options.ClientId ?? string.Empty)}&force_confirm=yes");

    public Task<DiskTokens> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var clean = (code ?? string.Empty).Trim();
        if (clean.Length is 0 or > 100)
        {
            throw new DiskException("Вставьте код, который показал Яндекс после разрешения доступа.");
        }

        return RequestAsync(new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = clean }, cancellationToken);
    }

    public Task<DiskTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        RequestAsync(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, cancellationToken);

    private async Task<DiskTokens> RequestAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new DiskException("Приложение Яндекса не настроено на сервере (Yandex__ClientId и Yandex__ClientSecret).");
        }

        form["client_id"] = _options.ClientId!;
        form["client_secret"] = _options.ClientSecret!;
        HttpResponseMessage response;
        try
        {
            using var content = new FormUrlEncodedContent(form);
            response = await http.PostAsync(new Uri(_options.OAuthBaseUrl, "token"), content, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new DiskException("Яндекс не отвечает — попробуйте позже.", inner: ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = Parse(body);
            var root = json?.RootElement;
            if (response.StatusCode == HttpStatusCode.OK && root?.TryGetProperty("access_token", out var access) == true)
            {
                var expiresIn = root.Value.TryGetProperty("expires_in", out var e) && e.TryGetInt64(out var seconds) ? seconds : 365L * 24 * 3600;
                var refresh = root.Value.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
                return new DiskTokens(access.GetString()!, refresh, clock.UtcNow.AddSeconds(expiresIn));
            }

            var error = root?.TryGetProperty("error", out var err) == true ? err.GetString() : null;
            throw error switch
            {
                "invalid_grant" when form["grant_type"] == "authorization_code" =>
                    new DiskException("Код не подошёл: он неверный, уже использован или устарел (живёт 10 минут). Получите новый код."),
                "invalid_grant" => new DiskException("Доступ к Диску отозван — подключите Диск заново.", unauthorized: true),
                "invalid_client" or "unauthorized_client" =>
                    new DiskException("Яндекс не узнал приложение: проверьте Yandex__ClientId и Yandex__ClientSecret на сервере."),
                _ => new DiskException($"Яндекс вернул ошибку {(int)response.StatusCode}{(error is null ? "" : $" ({error})")}."),
            };
        }
    }

    private static JsonDocument? Parse(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
