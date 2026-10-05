using LabScaner.Core.Abstractions;

namespace LabScaner.Infrastructure.Identity;

/// <summary>Преподаватель не определён: данные преподавателей не видны, запись запрещена.</summary>
public sealed class NoCurrentTeacher : ICurrentTeacher
{
    public static readonly NoCurrentTeacher Instance = new();

    public int? TeacherId => null;
}
