using LabScaner.Core.Directory;
using LabScaner.Core.Subjects;
using LabScaner.Core.Teaching;

namespace LabScaner.Core.Tests.Teaching;

public sealed class SubjectTermTests
{
    private static readonly Term Autumn = new(2026, TermSeason.Autumn, new DateOnly(2026, 12, 21), new DateOnly(2027, 1, 11));

    private static Subject Korpis() => new("Корпоративные информационные системы", "КорпИС");

    private static SubjectTerm Create(int labs = 0, bool coursework = false)
    {
        var st = new SubjectTerm(Korpis(), Autumn, 7, "КорпИС/2026-2027 КорпИС ИСТ - 7 сем");
        st.EnsureLabs(labs);
        st.SetCoursework(coursework);
        return st;
    }

    [Fact]
    public void SuggestPath_RendersSubjectTemplate()
    {
        var path = SubjectTerm.SuggestPath(Korpis(), Autumn, 7, "ИСТ");

        Assert.Equal("КорпИС/2026-2027 КорпИС ИСТ - 7 сем", path);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void StudySemester_OutOfRange_Throws(int semester) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubjectTerm(Korpis(), Autumn, semester, "КорпИС"));

    [Theory]
    [InlineData("")]
    [InlineData("КорпИС/{Учебный год}")]
    public void DiskRootPath_EmptyOrWithPlaceholders_Throws(string path) =>
        Assert.ThrowsAny<ArgumentException>(() => new SubjectTerm(Korpis(), Autumn, 7, path));

    [Fact]
    public void DiskRootPath_IsNormalized()
    {
        var st = Create();

        st.SetDiskRootPath(" /30 Политех//КорпИС/ ");

        Assert.Equal("30 Политех/КорпИС", st.DiskRootPath);
    }

    [Fact]
    public void EnsureLabs_NumbersInOrder_AndDoesNotDuplicate()
    {
        var st = Create(labs: 3);
        st.EnsureLabs(5);
        st.EnsureLabs(2);

        Assert.Equal([1, 2, 3, 4, 5], st.Labs.Select(l => l.Number));
        Assert.Equal("Лаб №5", st.Labs.Last().ShortName);
    }

    [Fact]
    public void AddLab_AndRemoveLastLab_KeepNumbersContiguous()
    {
        var st = Create(labs: 2);

        var lab = st.AddLab("Моделирование процессов");
        st.RemoveLastLab();
        st.RemoveLastLab();
        st.AddLab();

        Assert.Equal(3, lab.Number);
        Assert.Equal("Моделирование процессов", lab.Title);
        Assert.Equal([1, 2], st.Labs.Select(l => l.Number));
    }

    [Fact]
    public void AddLab_OverLimit_Throws()
    {
        var st = Create(labs: SubjectTerm.MaxLabs);

        Assert.Throws<InvalidOperationException>(() => st.AddLab());
    }

    [Fact]
    public void Coursework_AddedOnce_AndRemoved()
    {
        var st = Create(labs: 2, coursework: true);
        st.SetCoursework(true);

        Assert.Single(st.Assignments, a => a.Kind == AssignmentKind.Coursework);
        Assert.Equal("Курсовая", st.Coursework!.ShortName);

        st.SetCoursework(false);
        Assert.Null(st.Coursework);
        Assert.Equal(2, st.Assignments.Count);
    }

    [Fact]
    public void Deadline_FromCalendar_UnlessOverridden()
    {
        var st = Create(labs: 2, coursework: true);
        var lab1 = st.Labs.First();
        var lab2 = st.Labs.Last();

        lab2.Update("Своя", new DateOnly(2026, 11, 30), aiCheckEnabled: false);

        Assert.Equal(new DateOnly(2026, 12, 21), lab1.Deadline(Autumn));
        Assert.Equal(new DateOnly(2027, 1, 11), st.Coursework!.Deadline(Autumn));
        Assert.Equal(new DateOnly(2026, 11, 30), lab2.Deadline(Autumn));
        Assert.False(lab2.AiCheckEnabled);
        Assert.True(lab1.AiCheckEnabled);
    }

    [Fact]
    public void Assignment_TitleTooLong_Throws()
    {
        var lab = Create(labs: 1).Labs.First();

        Assert.Throws<ArgumentException>(() => lab.Update(new string('а', Assignment.MaxTitleLength + 1), null, true));
    }

    [Fact]
    public void Topics_SetChangeAndClear()
    {
        var st = Create(coursework: true);

        st.SetTopic(10, "  Морской порт ");
        st.SetTopic(11, "Языковая школа");
        st.SetTopic(10, "Ремонт телефонов");
        st.SetTopic(11, " ");

        Assert.Equal("Ремонт телефонов", st.TopicOf(10));
        Assert.Null(st.TopicOf(11));
        Assert.Single(st.Topics);
    }

    [Fact]
    public void Topic_TooLong_Throws() =>
        Assert.Throws<ArgumentException>(() => Create(coursework: true).SetTopic(1, new string('т', 301)));

    [Fact]
    public void Groups_AddedOnce_AndRemoved()
    {
        var st = Create();
        var ist41 = new Group("ИСТ-41");
        var ist42 = new Group("ИСТ-42");

        st.AddGroup(ist41);
        st.AddGroup(ist41);
        st.AddGroup(ist42);
        st.RemoveGroup(ist41);

        Assert.Equal([ist42], st.Groups);
    }

    [Fact]
    public void AssignTeacher_OtherTeacher_Throws()
    {
        var st = Create();
        st.AssignTeacher(1);

        Assert.Throws<InvalidOperationException>(() => st.AssignTeacher(2));
    }

    [Fact]
    public void Title_ShowsSubjectTermAndSemester() =>
        Assert.Equal("КорпИС, 2026-2027, осенний · 7 сем", Create().Title);
}
