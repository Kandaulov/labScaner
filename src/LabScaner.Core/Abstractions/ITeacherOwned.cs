namespace LabScaner.Core.Abstractions;

/// <summary>
/// Данные, принадлежащие преподавателю (ADR-016): предметы и всё под ними, письма, уведомления.
/// Чтение фильтруется по текущему преподавателю, владелец проставляется при сохранении.
/// </summary>
public interface ITeacherOwned
{
    int TeacherId { get; }

    /// <summary>Назначает владельца новой записи. Сменить владельца существующей записи нельзя.</summary>
    void AssignTeacher(int teacherId);
}
