using LabScaner.Core.Abstractions;

namespace LabScaner.Core.Connections;

/// <summary>
/// Яндекс Диск преподавателя (ADR-011, ADR-031): OAuth-токены зашифрованы (<see cref="ISecretProtector"/>),
/// владелец Диска и место — для показа. Один Диск на преподавателя.
/// </summary>
public sealed class DiskConnection : ITeacherOwned
{
    /// <summary>За сколько до истечения токен обновляется сам.</summary>
    public static readonly TimeSpan RefreshBefore = TimeSpan.FromDays(7);

    private DiskConnection()
    {
        AccessTokenProtected = string.Empty;
        Login = string.Empty;
    }

    public DiskConnection(string accessTokenProtected, string? refreshTokenProtected, DateTimeOffset expiresAt, DateTimeOffset connectedAt)
        : this()
    {
        SetTokens(accessTokenProtected, refreshTokenProtected, expiresAt);
        ConnectedAt = connectedAt;
    }

    public int Id { get; private set; }

    public int TeacherId { get; private set; }

    public string AccessTokenProtected { get; private set; }

    public string? RefreshTokenProtected { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }

    /// <summary>Логин Яндекса — чей это Диск.</summary>
    public string Login { get; private set; }

    public string? DisplayName { get; private set; }

    public long? TotalSpace { get; private set; }

    public long? UsedSpace { get; private set; }

    public DateTimeOffset? LastCheckAt { get; private set; }

    public bool? LastCheckOk { get; private set; }

    public string? LastCheckMessage { get; private set; }

    public bool NeedsRefresh(DateTimeOffset now) => ExpiresAt - now <= RefreshBefore;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public void SetTokens(string accessTokenProtected, string? refreshTokenProtected, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessTokenProtected);
        AccessTokenProtected = accessTokenProtected;
        RefreshTokenProtected = string.IsNullOrEmpty(refreshTokenProtected) ? RefreshTokenProtected : refreshTokenProtected;
        ExpiresAt = expiresAt;
    }

    public void SetAccount(DiskInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Login = info.Login.Length <= 100 ? info.Login : info.Login[..100];
        DisplayName = info.DisplayName is { Length: > 200 } name ? name[..200] : info.DisplayName;
        TotalSpace = info.TotalSpace;
        UsedSpace = info.UsedSpace;
    }

    public void RecordCheck(DateTimeOffset at, bool ok, string message)
    {
        LastCheckAt = at;
        LastCheckOk = ok;
        LastCheckMessage = message.Length <= 500 ? message : message[..500];
    }

    public void AssignTeacher(int teacherId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(teacherId);
        if (TeacherId != 0 && TeacherId != teacherId)
        {
            throw new InvalidOperationException("Подключение Диска принадлежит другому преподавателю.");
        }

        TeacherId = teacherId;
    }
}

/// <summary>Сведения о Диске: чей он и сколько места.</summary>
public sealed record DiskInfo(string Login, string? DisplayName, long TotalSpace, long UsedSpace);

/// <summary>Выданные Яндексом токены.</summary>
public sealed record DiskTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);

public enum DiskFolderState
{
    Exists,
    Missing,

    /// <summary>По этому пути лежит файл, а не папка.</summary>
    NotAFolder,
}

/// <summary>Ошибка Яндекса с понятным текстом; <see cref="Unauthorized"/> — токен недействителен.</summary>
public sealed class DiskException(string message, bool unauthorized = false, Exception? inner = null) : Exception(message, inner)
{
    public bool Unauthorized { get; } = unauthorized;
}
