using LabScaner.Core.Subjects;

namespace LabScaner.Core.Tests.Subjects;

public sealed class SubjectTests
{
    [Fact]
    public void AssignTeacher_SameTeacherTwice_Allowed()
    {
        var subject = new Subject("Операционные системы", "ОС");

        subject.AssignTeacher(7);
        subject.AssignTeacher(7);

        Assert.Equal(7, subject.TeacherId);
    }

    [Fact]
    public void AssignTeacher_AnotherTeacher_Throws()
    {
        var subject = new Subject("Операционные системы", "ОС");
        subject.AssignTeacher(7);

        Assert.Throws<InvalidOperationException>(() => subject.AssignTeacher(8));
    }

    [Fact]
    public void Constructor_TrimsNameAndCode()
    {
        var subject = new Subject("  Операционные системы ", " ОС ");

        Assert.Equal("Операционные системы", subject.Name);
        Assert.Equal("ОС", subject.Code);
        Assert.True(subject.IsActive);
    }
}
