using LabScaner.Core.Directory;
using LabScaner.Core.Subjects;
using LabScaner.Core.Teaching;

namespace LabScaner.Core.Tests.Teaching;

public sealed class SubjectTermTests
{
    private static readonly Term _autumn = new(2026, TermSeason.Autumn, new DateOnly(2026, 12, 21), new DateOnly(2027, 1, 11));

    private static Subject Korpis() => new("Корпоративные информационные системы", "КорпИС");

    private static SubjectTerm Create(int labs = 0, bool coursework = false)
    {
        var st = new SubjectTerm(Korpis(), _autumn, 7, "КорпИС/2026-2027 КорпИС ИСТ - 7 сем");
        st.EnsureLabs(labs);
        st.SetCoursework(coursework);
        return st;
    }

    [Fact]
    public void SuggestPath_RendersSubjectTemplate()
    {
        var path = SubjectTerm.SuggestPath(Korpis(), _autumn, 7, "ИСТ");

        Assert.Equal("КорпИС/2026-2027 КорпИС ИСТ - 7 сем", path);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void StudySemester_OutOfRange_Throws(int semester) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubjectTerm(Korpis(), _autumn, semester, "КорпИС"));

    [Theory]
    [InlineData("")]
    [InlineData("КорпИС/{Учебный год}")]
    public void DiskRootPath_EmptyOrWithPlaceholders_Throws(string path) =>
        Assert.ThrowsAny<ArgumentException>(() => new SubjectTerm(Korpis(), _autumn, 7, path));

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

        Assert.Equal(new DateOnly(2026, 12, 21), lab1.Deadline(_autumn));
        Assert.Equal(new DateOnly(2027, 1, 11), st.Coursework!.Deadline(_autumn));
        Assert.Equal(new DateOnly(2026, 11, 30), lab2.Deadline(_autumn));
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

    private static LabTask LabTaskOf(int n, string title = "") =>
        new(n, $"Лабораторная работа №{n}", title, $"Задание {n}", [$"Пункт {n}.1", $"Пункт {n}.2"]);

    [Fact]
    public void ApplyLabTasks_AddsLabs_SetsTasks_KeepsTitleWhenEmpty_RemovesMissing()
    {
        var st = Create(labs: 5);
        st.Labs.First().Update("Старое название", new DateOnly(2026, 11, 1), aiCheckEnabled: false);

        st.ApplyLabTasks([LabTaskOf(1), LabTaskOf(2, "ETL"), LabTaskOf(3, "RabbitMQ")], new Dictionary<int, string> { [3] = "Очереди RabbitMQ" }, removeMissing: true);

        Assert.Equal([1, 2, 3], st.Labs.Select(l => l.Number));
        var lab1 = st.Labs.First();
        Assert.Equal("Старое название", lab1.Title);
        Assert.Equal(new DateOnly(2026, 11, 1), lab1.DeadlineOverride);
        Assert.False(lab1.AiCheckEnabled);
        Assert.Equal("Задание 1", lab1.TaskText);
        Assert.Equal(["Пункт 1.1", "Пункт 1.2"], lab1.Checklist);
        Assert.Equal(["Старое название", "ETL", "Очереди RabbitMQ"], st.Labs.Select(l => l.Title));
    }

    [Fact]
    public void ApplyLabTasks_KeepsExtraLabs_WhenAsked_AndAddsMissingOnes()
    {
        var st = Create(labs: 2);

        st.ApplyLabTasks([LabTaskOf(4)], null, removeMissing: false);

        Assert.Equal([1, 2, 3, 4], st.Labs.Select(l => l.Number));
        Assert.False(st.Labs.First().HasTask);
        Assert.True(st.Labs.Last().HasTask);
    }

    [Fact]
    public void ApplyLabTasks_EmptyOrTooMany_Throws()
    {
        var st = Create();

        Assert.Throws<ArgumentException>(() => st.ApplyLabTasks([], null, false));
        Assert.Throws<ArgumentException>(() => st.ApplyLabTasks([LabTaskOf(SubjectTerm.MaxLabs + 1)], null, false));
    }

    [Fact]
    public void SetTask_CleansChecklist_AndValidatesLength()
    {
        var lab = Create(labs: 1).Labs.First();

        lab.SetTask(" Текст\r\nзадания ", ["- Первый", "", "  ", "• Второй", "первый"]);

        Assert.Equal("Текст\nзадания", lab.TaskText);
        Assert.Equal(["Первый", "Второй"], lab.Checklist);
        Assert.Throws<ArgumentException>(() => lab.SetTask(new string('т', Assignment.MaxTaskTextLength + 1), []));
        Assert.Throws<ArgumentException>(() => lab.SetTask("т", [new string('п', Assignment.MaxChecklistItemLength + 1)]));
        Assert.Throws<ArgumentException>(() => lab.SetTask("т", Enumerable.Range(1, Assignment.MaxChecklistItems + 1).Select(i => $"Пункт {i}")));
    }

    [Fact]
    public void CopyWorksFrom_CopiesLabsTasksCourseworkAndRequirements_KeepsOwnDeadlines()
    {
        var source = Create(labs: 3, coursework: true);
        source.ApplyLabTasks([LabTaskOf(1, "IDEF0"), LabTaskOf(2, "ER"), LabTaskOf(3, "API")], null, removeMissing: true);
        source.Labs.Last().Update("API", null, aiCheckEnabled: false);
        source.ApplyCourseworkTask(new CourseworkTask("Курсовая: сети", ["Структура ПЗ"], []));
        source.SetGeneralRequirements("Титульный лист обязателен.");

        var target = new SubjectTerm(Korpis(), _autumn, 7, "КорпИС");
        target.EnsureLabs(5);
        target.Labs.First().Update(null, new DateOnly(2026, 12, 1), true);
        target.CopyWorksFrom(source);

        Assert.Equal(["IDEF0", "ER", "API"], target.Labs.Select(l => l.Title));
        Assert.Equal(new DateOnly(2026, 12, 1), target.Labs.First().DeadlineOverride);
        Assert.False(target.Labs.Last().AiCheckEnabled);
        Assert.Equal("Задание 2", target.Labs.ElementAt(1).TaskText);
        Assert.Equal("Курсовая: сети", target.Coursework!.TaskText);
        Assert.Equal(["Структура ПЗ"], target.Coursework.Checklist);
        Assert.Equal("Титульный лист обязателен.", target.GeneralRequirements);

        Assert.Throws<ArgumentException>(() => target.CopyWorksFrom(target));
    }

    [Fact]
    public void TaskDocument_HashesContentAndCleansName()
    {
        var doc = new TaskDocument(1, TaskDocumentKind.Labs, @"C:\Users\Препод\задания.docx", [1, 2, 3], DateTimeOffset.UnixEpoch);

        Assert.Equal("задания.docx", doc.FileName);
        Assert.Equal(3, doc.Size);
        Assert.Equal("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81", doc.Sha256);
        Assert.Null(doc.AppliedAt);
        Assert.Throws<ArgumentException>(() => new TaskDocument(1, TaskDocumentKind.Labs, "x.docx", [], DateTimeOffset.UnixEpoch));
    }
}
