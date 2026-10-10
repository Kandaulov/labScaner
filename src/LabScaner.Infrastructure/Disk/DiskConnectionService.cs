using LabScaner.Core.Abstractions;
using LabScaner.Core.Connections;
using LabScaner.Core.Teaching;
using LabScaner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LabScaner.Infrastructure.Disk;

/// <summary>Состояние папок предмета в семестре на Диске.</summary>
public sealed record FolderReport(int SubjectTermId, string Title, string RootPath, DiskFolderState Root, IReadOnlyList<string> MissingSubfolders, string? Error = null)
{
    public bool Complete => Error is null && Root == DiskFolderState.Exists && MissingSubfolders.Count == 0;
}

/// <summary>
/// Подключение Яндекс Диска преподавателя (ADR-031): код → токены (зашифрованы), автообновление токена,
/// проверка и создание папок семестров. Ничего не удаляет и не перезаписывает (ADR-011).
/// </summary>
public sealed class DiskConnectionService(LabScanerDbContext db, ISecretProtector secrets, IYandexOAuth oauth, IYandexDisk disk, IClock clock)
{
    public Task<DiskConnection?> FindAsync(CancellationToken cancellationToken = default) =>
        db.DiskConnections.SingleOrDefaultAsync(cancellationToken);

    /// <summary>Код подтверждения → токены → сведения о Диске. Повторное подключение заменяет токены.</summary>
    /// <exception cref="DiskException">Код не подошёл или Яндекс недоступен.</exception>
    public async Task<DiskConnection> ConnectAsync(string code, CancellationToken cancellationToken = default)
    {
        var tokens = await oauth.ExchangeCodeAsync(code, cancellationToken);
        var info = await disk.GetInfoAsync(tokens.AccessToken, cancellationToken);
        var now = clock.UtcNow;
        var connection = await FindAsync(cancellationToken);
        var access = secrets.Protect(tokens.AccessToken);
        var refresh = tokens.RefreshToken is null ? null : secrets.Protect(tokens.RefreshToken);
        if (connection is null)
        {
            connection = new DiskConnection(access, refresh, tokens.ExpiresAt, now);
            db.DiskConnections.Add(connection);
        }
        else
        {
            connection.SetTokens(access, refresh, tokens.ExpiresAt);
        }

        connection.SetAccount(info);
        connection.RecordCheck(now, true, Describe(info));
        await db.SaveChangesAsync(cancellationToken);
        return connection;
    }

    /// <summary>Сведения о Диске заново; итог записывается в подключение.</summary>
    public async Task<bool> CheckAsync(DiskConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        try
        {
            var info = await disk.GetInfoAsync(await AccessTokenAsync(connection, cancellationToken), cancellationToken);
            connection.SetAccount(info);
            connection.RecordCheck(clock.UtcNow, true, Describe(info));
        }
        catch (DiskException ex)
        {
            connection.RecordCheck(clock.UtcNow, false, ex.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return connection.LastCheckOk == true;
    }

    /// <summary>Действующий токен: обновляется сам за неделю до истечения.</summary>
    /// <exception cref="DiskException">Токен не расшифровывается, истёк или отозван.</exception>
    public async Task<string> AccessTokenAsync(DiskConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var now = clock.UtcNow;
        var refresh = connection.RefreshTokenProtected is null ? null : secrets.Unprotect(connection.RefreshTokenProtected);
        if (connection.NeedsRefresh(now) && refresh is not null)
        {
            try
            {
                var tokens = await oauth.RefreshAsync(refresh, cancellationToken);
                connection.SetTokens(secrets.Protect(tokens.AccessToken), tokens.RefreshToken is null ? null : secrets.Protect(tokens.RefreshToken), tokens.ExpiresAt);
                await db.SaveChangesAsync(cancellationToken);
                return tokens.AccessToken;
            }
            catch (DiskException) when (!connection.IsExpired(now))
            {
                // Обновить не удалось, но старый токен ещё действует — работаем с ним.
            }
        }

        if (connection.IsExpired(now))
        {
            throw new DiskException("Срок доступа к Диску истёк — подключите Диск заново.", unauthorized: true);
        }

        return secrets.Unprotect(connection.AccessTokenProtected)
            ?? throw new DiskException("Токен Диска не расшифровывается (сменились ключи шифрования на сервере) — подключите Диск заново.", unauthorized: true);
    }

    /// <summary>Папки предметов в семестрах: корневая и подпапки групп (ADR-011).</summary>
    public async Task<IReadOnlyList<FolderReport>> CheckFoldersAsync(DiskConnection connection, IEnumerable<SubjectTerm> subjectTerms, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subjectTerms);
        var token = await AccessTokenAsync(connection, cancellationToken);
        var reports = new List<FolderReport>();
        foreach (var st in subjectTerms)
        {
            reports.Add(await CheckOneAsync(token, st, cancellationToken));
        }

        return reports;
    }

    /// <summary>
    /// Создаёт недостающие подпапки; корневую папку семестра — только если явно попросили (<paramref name="createRoot"/>):
    /// обычно она уже есть, а её отсутствие чаще значит опечатку в пути.
    /// </summary>
    public async Task<FolderReport> CreateFoldersAsync(DiskConnection connection, SubjectTerm subjectTerm, bool createRoot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subjectTerm);
        var token = await AccessTokenAsync(connection, cancellationToken);
        var before = await CheckOneAsync(token, subjectTerm, cancellationToken);
        if (before.Error is not null || before.Root == DiskFolderState.NotAFolder || (before.Root == DiskFolderState.Missing && !createRoot))
        {
            return before;
        }

        try
        {
            if (before.Root == DiskFolderState.Missing)
            {
                await disk.CreateFolderAsync(token, subjectTerm.DiskRootPath, cancellationToken);
            }

            foreach (var name in before.Root == DiskFolderState.Missing ? subjectTerm.DiskSubfolders() : before.MissingSubfolders)
            {
                await disk.CreateFolderAsync(token, $"{subjectTerm.DiskRootPath}/{name}", cancellationToken);
            }
        }
        catch (DiskException ex)
        {
            return before with { Error = ex.Message };
        }

        return await CheckOneAsync(token, subjectTerm, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var connection = await FindAsync(cancellationToken);
        if (connection is not null)
        {
            db.DiskConnections.Remove(connection);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<FolderReport> CheckOneAsync(string token, SubjectTerm st, CancellationToken cancellationToken)
    {
        var title = st.Title;
        try
        {
            var root = await disk.GetFolderStateAsync(token, st.DiskRootPath, cancellationToken);
            var missing = new List<string>();
            if (root == DiskFolderState.Exists)
            {
                foreach (var name in st.DiskSubfolders())
                {
                    if (await disk.GetFolderStateAsync(token, $"{st.DiskRootPath}/{name}", cancellationToken) != DiskFolderState.Exists)
                    {
                        missing.Add(name);
                    }
                }
            }

            return new FolderReport(st.Id, title, st.DiskRootPath, root, missing);
        }
        catch (DiskException ex)
        {
            return new FolderReport(st.Id, title, st.DiskRootPath, DiskFolderState.Missing, [], ex.Message);
        }
    }

    private static string Describe(DiskInfo info) =>
        $"Диск {info.Login}: занято {Gb(info.UsedSpace)} из {Gb(info.TotalSpace)}.";

    public static string Gb(long bytes) =>
        (bytes / 1024d / 1024 / 1024).ToString(bytes >= 10L * 1024 * 1024 * 1024 ? "0" : "0.0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + " ГБ";
}
