using LabScaner.Core.Abstractions;

namespace LabScaner.Core.Teaching;

/// <summary>Тема курсовой студента в предмете в семестре (ADR-025).</summary>
public sealed class CourseworkTopic : ITeacherOwned
{
    private CourseworkTopic()
    {
        Topic = string.Empty;
    }

    internal CourseworkTopic(SubjectTerm subjectTerm, int studentId, string topic)
    {
        SubjectTerm = subjectTerm;
        StudentId = studentId;
        Topic = topic.Trim();
    }

    public int Id { get; private set; }

    public int TeacherId { get; private set; }

    public int SubjectTermId { get; private set; }

    public SubjectTerm? SubjectTerm { get; private set; }

    public int StudentId { get; private set; }

    public string Topic { get; private set; }

    internal void Change(string topic) => Topic = topic.Trim();

    public void AssignTeacher(int teacherId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(teacherId);
        if (TeacherId != 0 && TeacherId != teacherId)
        {
            throw new InvalidOperationException("Тема принадлежит другому преподавателю.");
        }

        TeacherId = teacherId;
    }
}
