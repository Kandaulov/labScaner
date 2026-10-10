namespace LabScaner.Core.Abstractions;

/// <summary>
/// Шифрование секретов подключений (пароль почты, токен Диска, ключ ИИ) перед записью в БД.
/// Реализация — ASP.NET Core Data Protection: ключи лежат на сервере в томе Docker, не в БД.
/// </summary>
public interface ISecretProtector
{
    string Protect(string secret);

    /// <summary>Расшифровка; <c>null</c>, если ключи шифрования потеряны или значение повреждено — секрет нужно ввести заново.</summary>
    string? Unprotect(string protectedSecret);
}
