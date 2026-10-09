using LabScaner.Core.Directory;

namespace LabScaner.Core.Tests.Directory;

public sealed class GroupListParserTests
{
    private static IReadOnlyList<IReadOnlyList<string?>> Rows(params string?[][] rows) => rows;

    [Theory]
    [InlineData("ИСТ41", "ИСТ-41")]
    [InlineData("ИСТ-42", "ИСТ-42")]
    [InlineData("ИСТбд-41", "ИСТбд-41")]
    [InlineData("Лист1", null)]
    [InlineData("Sheet", null)]
    [InlineData("2026", null)]
    public void TryGroupName_FromSheetName(string sheet, string? expected) =>
        Assert.Equal(expected, GroupListParser.TryGroupName(sheet));

    [Fact]
    public void Parse_FixtureLikeSheet()
    {
        var sheet = GroupListParser.Parse("ИСТ41", Rows(
            ["№", "ФИО", "Тема КР"],
            ["1", "НАЗАРОВ ОЛЕГ ИГОРЕВИЧ", null],
            ["2", "БЛИНОВА УЛЬЯНА БОРИСОВНА", "Морской порт"],
            [],
            ["3", "Юдин Роман", "Ремонт телефонов"]));

        Assert.Null(sheet.Error);
        Assert.Equal("ИСТ-41", sheet.GroupName);
        Assert.Equal(3, sheet.ValidRows.Count());
        Assert.Equal("Назаров Олег Игоревич", sheet.Rows[0].Name!.FullName);
        Assert.Equal("Морской порт", sheet.Rows[1].Topic);
        Assert.Equal(5, sheet.Rows[2].RowNumber);
    }

    [Fact]
    public void Parse_HeaderNotInFirstRow_AndEmailColumn()
    {
        var sheet = GroupListParser.Parse("ИСТ-42", Rows(
            ["Список группы ИСТ-42"],
            [],
            ["№", "Студент", "E-mail"],
            ["1", "Иванов Иван", "Ivanov@Mail.RU"]));

        var row = Assert.Single(sheet.Rows);
        Assert.Equal("ivanov@mail.ru", row.Email);
        Assert.Equal(4, row.RowNumber);
    }

    [Fact]
    public void Parse_RowErrors()
    {
        var sheet = GroupListParser.Parse("ИСТ41", Rows(
            ["№", "ФИО", "E-mail"],
            ["1", "Иванов", null],
            ["2", "[?]", null],
            ["3", "Петров Пётр", "not-an-email"],
            ["4", "Сидоров Иван Иванович", null],
            ["5", "СИДОРОВ ИВАН ИВАНОВИЧ", null],
            ["6", null, "x@y.ru"]));

        Assert.Equal("Нужны хотя бы фамилия и имя", sheet.Rows[0].Error);
        Assert.Equal("ФИО содержит недопустимые символы", sheet.Rows[1].Error);
        Assert.StartsWith("Некорректный e-mail", sheet.Rows[2].Error, StringComparison.Ordinal);
        Assert.Null(sheet.Rows[3].Error);
        Assert.Equal("Повтор строки 5", sheet.Rows[4].Error);
        Assert.Equal("Пустое ФИО", sheet.Rows[5].Error);
        Assert.Single(sheet.ValidRows);
    }

    [Fact]
    public void Parse_NoHeader_IsSheetError() =>
        Assert.NotNull(GroupListParser.Parse("ИСТ41", Rows(["1", "Иванов Иван"])).Error);

    [Fact]
    public void Parse_NotAGroupSheet_IsSheetError() =>
        Assert.Contains("не похоже на название группы", GroupListParser.Parse("Лист1", Rows(["ФИО"])).Error, StringComparison.Ordinal);
}

public sealed class StudentNamesTests
{
    [Theory]
    [InlineData("Иванов Иван Иванович", "ИВАНОВ ИВАН ИВАНОВИЧ", true)]
    [InlineData("Иванов Иван", "Иванов Иван Иванович", true)]
    [InlineData("Журавлёв Семён", "Журавлев Семен Павлович", true)]
    [InlineData("Иванов Иван Иванович", "Иванов Иван Петрович", false)]
    [InlineData("Иванов Иван", "Иванова Ивана", false)]
    public void Same(string a, string b, bool expected) =>
        Assert.Equal(expected, StudentNames.Same(PersonName.Parse(a), PersonName.Parse(b)));

    [Fact]
    public void RefineName_FillsMiddleName_ButNotSurname()
    {
        var student = new Group("ИСТ-42").AddStudent(PersonName.Parse("Юдин Роман"));

        Assert.True(student.RefineName(PersonName.Parse("ЮДИН РОМАН СЕРГЕЕВИЧ")));
        Assert.Equal("Юдин Роман Сергеевич", student.Name.FullName);
        Assert.False(student.RefineName(PersonName.Parse("Юдин Роман Сергеевич")));
        Assert.Throws<InvalidOperationException>(() => student.RefineName(PersonName.Parse("Петров Роман")));
    }
}
