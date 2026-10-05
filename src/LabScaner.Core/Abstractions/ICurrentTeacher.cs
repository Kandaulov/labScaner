namespace LabScaner.Core.Abstractions;

/// <summary>
/// Преподаватель, от имени которого идёт работа: пользователь веб-интерфейса или ящик, который
/// обрабатывает фоновая задача. Данные преподавателей изолированы по нему (ADR-016).
/// </summary>
public interface ICurrentTeacher
{
    /// <summary><c>null</c> — преподаватель не определён (анонимный запрос, системная задача).</summary>
    int? TeacherId { get; }
}
