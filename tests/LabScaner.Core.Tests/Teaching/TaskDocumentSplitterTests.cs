using LabScaner.Core.Teaching;

namespace LabScaner.Core.Tests.Teaching;

public sealed class TaskDocumentSplitterTests
{
    private static DocParagraph H1(string text) => new(text, HeadingLevel: 1);

    private static DocParagraph H2(string text) => new(text, HeadingLevel: 2);

    private static DocParagraph P(string text) => new(text);

    private static DocParagraph Li(string text, int level = 0) => new(text, ListLevel: level);

    private static readonly string _filler = new('х', TaskDocumentSplitter.ShortTaskLength);

    [Fact]
    public void Split_ByHeadingStyle_PreambleTitlesTextAndChecklist()
    {
        var split = TaskDocumentSplitter.Split(
        [
            new DocParagraph("Требования и задания по дисциплине", HeadingLevel: 0),
            H1("Требования по оформлению и сдаче работ"),
            P("Отчёт с титульным листом."),
            H1("Лабораторная работа №1"),
            H2("Цель:"),
            P("Создание сервиса загрузки транспортных файлов. Подробности ниже."),
            P(_filler),
            H2("Ожидаемый результат:"),
            P("В работе должен быть реализован конвейер:"),
            Li("Extract: извлечение."),
            Li("Transform: трансформация."),
            P("Сервис проверяет папку по расписанию."),
            H2("Отчет по работе:"),
            P("Код сервиса, описание классов."),
            H1("Лабораторная работа № 2. Логирование"),
            P("Требуется дополнить конвейер логированием из работы №1. " + _filler),
        ]);

        Assert.Equal([1, 2], split.Labs.Select(l => l.Number));
        Assert.StartsWith("Требования и задания по дисциплине", split.Preamble, StringComparison.Ordinal);
        Assert.Contains("Отчёт с титульным листом.", split.Preamble, StringComparison.Ordinal);

        var lab1 = split.Labs[0];
        Assert.Equal("Создание сервиса загрузки транспортных файлов", lab1.Title);
        Assert.Contains("- Extract: извлечение.", lab1.Text, StringComparison.Ordinal);
        Assert.Contains("\n\nОжидаемый результат:", lab1.Text, StringComparison.Ordinal);
        Assert.Equal(
            ["Extract: извлечение", "Transform: трансформация", "Сервис проверяет папку по расписанию", "В отчёте: Код сервиса, описание классов"],
            lab1.Checklist);

        Assert.Equal("Логирование", split.Labs[1].Title);
        Assert.Empty(split.Warnings);
    }

    [Fact]
    public void Split_ReferencesInsideText_AreNotHeadings()
    {
        var split = TaskDocumentSplitter.Split(
        [
            H1("Лабораторная работа №1"),
            P("Цель: реализовать API"),
            P("Лабораторная работа №2 опирается на эту работу, поэтому сохраните код."),
            Li("Лабораторная работа №3 — тоже"),
            H1("Лабораторная работа №2"),
            P("Цель: ETL"),
        ]);

        Assert.Equal([1, 2], split.Labs.Select(l => l.Number));
        Assert.Equal("реализовать API", split.Labs[0].Title);
        Assert.Contains("опирается на эту работу", split.Labs[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Split_WithoutHeadingStyles_UsesBoldThenShortParagraphs()
    {
        var bold = TaskDocumentSplitter.Split(
        [
            new DocParagraph("ЛАБОРАТОРНАЯ РАБОТА 1", IsBold: true),
            P("Лабораторная работа 2 выполняется после первой."),
            new DocParagraph("Лабораторная работа №2: Логирование", IsBold: true),
        ]);
        Assert.Equal([1, 2], bold.Labs.Select(l => l.Number));
        Assert.Equal("Логирование", bold.Labs[1].Title);

        var plain = TaskDocumentSplitter.Split(
        [
            P("Лабораторная работа №1"),
            P("Цель: ETL. " + _filler),
            P("Лабораторная работа №2 выполняется после первой и использует её результаты — это длинный абзац, а не заголовок."),
            P("Лабораторная работа №2"),
            P("Цель: логирование. " + _filler),
        ]);
        Assert.Equal([1, 2], plain.Labs.Select(l => l.Number));
        Assert.Contains("выполняется после первой", plain.Labs[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Split_NoHeadings_ReturnsWarningAndWholeTextAsPreamble()
    {
        var split = TaskDocumentSplitter.Split([P("Курсовая работа"), P("Текст")]);

        Assert.Empty(split.Labs);
        Assert.Contains("ни одного заголовка", split.Warnings[0], StringComparison.Ordinal);
        Assert.Equal("Курсовая работа\nТекст", split.Preamble.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Split_WarnsAboutGapsDuplicatesShortTextAndMissingTitle()
    {
        var split = TaskDocumentSplitter.Split(
        [
            H1("Лабораторная работа №1"),
            P("Все будет.."),
            H1("Лабораторная работа №3"),
            P("Цель: API. " + _filler),
            H1("Лабораторная работа №3"),
            P("Дубликат"),
        ]);

        Assert.Equal([1, 3], split.Labs.Select(l => l.Number));
        Assert.Contains(split.Warnings, w => w.Contains("№3 встречается в документе дважды", StringComparison.Ordinal));
        Assert.Contains(split.Warnings, w => w.Contains("нет лабораторных №2", StringComparison.Ordinal));
        Assert.Contains(split.Warnings, w => w.Contains("№1 короткое", StringComparison.Ordinal));
        Assert.Contains(split.Warnings, w => w.Contains("У лабораторной №1 нет названия", StringComparison.Ordinal));
    }

    [Fact]
    public void Split_OtherTopLevelHeading_EndsLabAndGoesToPreamble()
    {
        var split = TaskDocumentSplitter.Split(
        [
            H1("Лабораторная работа №1"),
            P("Цель: ETL. " + _filler),
            H1("Список литературы"),
            P("Книга 1"),
        ]);

        Assert.DoesNotContain("Книга 1", split.Labs[0].Text, StringComparison.Ordinal);
        Assert.Contains("Книга 1", split.Preamble, StringComparison.Ordinal);
        Assert.Contains(split.Warnings, w => w.Contains("«Список литературы»", StringComparison.Ordinal));
    }

    [Fact]
    public void Checklist_NestedItemsJoinedToParent_LongOnesSplit()
    {
        var longChild = new string('д', 200);
        var split = TaskDocumentSplitter.Split(
        [
            H1("Лабораторная работа №1"),
            H2("Ожидаемый результат:"),
            P("В лог должны писаться события:"),
            Li("Проверки наличия файлов"),
            Li("Дата и время проверки", 1),
            Li("Результат проверки", 1),
            Li("Запросы к API:"),
            Li(longChild, 1),
            Li(longChild, 1),
            Li("…"),
            P(_filler),
        ]);

        Assert.Equal(
            ["Проверки наличия файлов: Дата и время проверки; Результат проверки", "Запросы к API:", longChild, longChild, _filler],
            split.Labs[0].Checklist);
    }

    [Fact]
    public void ParseCoursework_ChecklistFromCriteriaSection()
    {
        var task = TaskDocumentSplitter.ParseCoursework(
        [
            H1("Тема курсовой работы"),
            P("Проектирование сетей. " + _filler),
            H2("Критерии оценки работы"),
            Li("Соответствие ПЗ требуемой структуре"),
            Li("Проработанность структуры сети"),
            Li("Рассмотрены ограничения", 1),
            H1("Примеры тем курсовой"),
            P("КИС почтовой службы"),
        ]);

        Assert.Equal(["Соответствие ПЗ требуемой структуре", "Проработанность структуры сети: Рассмотрены ограничения"], task.Checklist);
        Assert.Contains("КИС почтовой службы", task.Text, StringComparison.Ordinal);
        Assert.Empty(task.Warnings);
    }

    [Fact]
    public void ParseCoursework_NoCriteria_Warns()
    {
        var task = TaskDocumentSplitter.ParseCoursework([P("Тема: сети")]);

        Assert.Empty(task.Checklist);
        Assert.Equal(2, task.Warnings.Count);
    }
}
