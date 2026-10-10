using LabScaner.Core.Connections;

namespace LabScaner.Core.Abstractions;

/// <summary>OAuth Яндекса (ADR-031): код подтверждения → токены, обновление токенов.</summary>
public interface IYandexOAuth
{
    /// <summary>Настроено ли приложение Яндекса на сервере (ClientId и секрет).</summary>
    bool IsConfigured { get; }

    /// <summary>Ссылка, по которой преподаватель разрешает доступ и получает код.</summary>
    Uri AuthorizeUrl { get; }

    /// <exception cref="DiskException">Код неверный или устарел.</exception>
    Task<DiskTokens> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <exception cref="DiskException">Токен обновления отозван — нужно подключить заново.</exception>
    Task<DiskTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// Яндекс Диск преподавателя. Только чтение и создание папок (позже — загрузка новых файлов):
/// операций удаления, переименования и перезаписи нет и не будет (ADR-011).
/// </summary>
public interface IYandexDisk
{
    /// <exception cref="DiskException">Нет доступа или Диск не отвечает.</exception>
    Task<DiskInfo> GetInfoAsync(string accessToken, CancellationToken cancellationToken = default);

    Task<DiskFolderState> GetFolderStateAsync(string accessToken, string path, CancellationToken cancellationToken = default);

    /// <summary>Создаёт папку и недостающие папки над ней; существующая папка — не ошибка.</summary>
    Task CreateFolderAsync(string accessToken, string path, CancellationToken cancellationToken = default);
}
