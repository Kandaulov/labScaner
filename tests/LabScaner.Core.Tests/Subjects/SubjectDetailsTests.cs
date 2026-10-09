using LabScaner.Core.Subjects;

namespace LabScaner.Core.Tests.Subjects;

public sealed class SubjectDetailsTests
{
    private static Subject Korpis(params string[] aliases)
    {
        var subject = new Subject("Корпоративные информационные системы", "КорпИС");
        subject.Update(subject.Name, "КорпИС", aliases, FinalAssessment.Exam, null, DiskPathTemplate.Default);
        return subject;
    }

    [Theory]
    [InlineData("КорпИС")]
    [InlineData("корпис")]
    [InlineData("Корп ИС")]
    [InlineData("КОРП-ИС")]
    [InlineData("кис")]
    public void Matches_CodeAndAliases_IgnoringCaseSpacesDashes(string text) =>
        Assert.True(Korpis("КИС").Matches(text));

    [Fact]
    public void Matches_OtherWord_False() => Assert.False(Korpis("КИС").Matches("ОС"));

    [Fact]
    public void Update_DropsDuplicateAndCodeLikeAliases()
    {
        // «Корп ИС» и «корпис» совпадают с кодом без пробелов и регистра — лишние.
        var subject = Korpis("КИС", "кис", "Корп ИС", "корпис", "  ", "КорпИнфСист");

        Assert.Equal(["КИС", "КорпИнфСист"], subject.Aliases);
    }

    [Theory]
    [InlineData("", "КорпИС")]
    [InlineData("Название", "")]
    [InlineData("Название", "Корп – ИС")]
    public void Update_Invalid_Throws(string name, string code) =>
        Assert.Throws<ArgumentException>(() => new Subject(name, code));

    [Fact]
    public void ConflictWith_SameSpellingInAnotherSubject()
    {
        var korpis = Korpis("КИС");
        var other = new Subject("Компьютерные информационные системы", "КИС");

        Assert.Contains("КИС", other.ConflictWith([korpis]), StringComparison.Ordinal);
        Assert.Null(new Subject("Операционные системы", "ОС").ConflictWith([korpis]));
    }

    [Fact]
    public void FinalAssessment_AutomatMark()
    {
        Assert.Equal("5", FinalAssessment.Exam.AutomatMark());
        Assert.Equal("зачтено", FinalAssessment.Pass.AutomatMark());
        Assert.Equal("Дифференцированный зачёт", FinalAssessment.GradedPass.Title());
    }
}

public sealed class DiskPathTemplateTests
{
    private const string Template = "30 Политех/03 {Код}/10 {Код} - Отчетные документы/{Учебный год} {Код} {Направление} - {N} сем";

    [Fact]
    public void Render_SubstitutesAllPlaceholders() =>
        Assert.Equal(
            "30 Политех/03 КорпИС/10 КорпИС - Отчетные документы/2026-2027 КорпИС ИСТ - 7 сем",
            DiskPathTemplate.Render(Template, new DiskPathValues("КорпИС", "2026-2027", "ИСТ", 7)));

    [Theory]
    [InlineData(@"\30 Политех\\03 КорпИС\", "30 Политех/03 КорпИС")]
    [InlineData(" / a / b / ", "a/b")]
    public void Normalize_SlashesAndSpaces(string raw, string expected) =>
        Assert.Equal(expected, DiskPathTemplate.Normalize(raw));

    [Theory]
    [InlineData(Template, null)]
    [InlineData("{Предмет}/{N}", "Неизвестные подстановки: {Предмет}")]
    [InlineData("a/../b", "Сегменты")]
    [InlineData("a:b", "недопустимые")]
    [InlineData("  ", "пуст")]
    public void Validate(string template, string? errorPart)
    {
        var error = DiskPathTemplate.Validate(template);
        if (errorPart is null)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.Contains(errorPart, error, StringComparison.Ordinal);
        }
    }
}
