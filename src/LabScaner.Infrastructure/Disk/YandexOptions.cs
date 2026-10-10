namespace LabScaner.Infrastructure.Disk;

/// <summary>
/// Приложение Яндекса (ADR-031): ClientId и секрет — из переменных окружения Yandex__ClientId и Yandex__ClientSecret.
/// Адреса API меняются только в тестах.
/// </summary>
public sealed class YandexOptions
{
    public const string Section = "Yandex";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public Uri OAuthBaseUrl { get; set; } = new("https://oauth.yandex.ru/");

    public Uri DiskApiBaseUrl { get; set; } = new("https://cloud-api.yandex.net/");
}
