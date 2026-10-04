using LabScaner.Core.Directory;

namespace LabScaner.Core.Tests.Directory;

public sealed class StudentTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static Student NewStudent() =>
        new Group("ИСТ-41").AddStudent(PersonName.Parse("Иванов Иван Иванович"));

    [Fact]
    public void AddEmail_NormalizesAddress()
    {
        var email = NewStudent().AddEmail("  Ivanov@Mail.RU ", EmailSource.Auto, _now);

        Assert.Equal("ivanov@mail.ru", email.Email);
    }

    [Fact]
    public void AddEmail_SameAddressTwice_NoDuplicate()
    {
        var student = NewStudent();

        student.AddEmail("ivanov@mail.ru", EmailSource.Auto, _now);
        student.AddEmail("IVANOV@mail.ru", EmailSource.Auto, _now.AddDays(1));

        Assert.Single(student.Emails);
    }

    [Fact]
    public void AddEmail_ManualAfterAuto_UpgradesSource()
    {
        var student = NewStudent();
        student.AddEmail("ivanov@mail.ru", EmailSource.Auto, _now);

        var email = student.AddEmail("ivanov@mail.ru", EmailSource.Manual, _now);

        Assert.Equal(EmailSource.Manual, email.Source);
    }

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("@mail.ru")]
    [InlineData("a@b@c")]
    [InlineData("ivanov@")]
    public void AddEmail_Invalid_Throws(string email) =>
        Assert.Throws<FormatException>(() => NewStudent().AddEmail(email, EmailSource.Manual, _now));

    [Fact]
    public void NewStudent_IsActive_UntilDeactivated()
    {
        var student = NewStudent();
        Assert.True(student.IsActive);

        student.Deactivate();

        Assert.False(student.IsActive);
    }
}
