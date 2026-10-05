using LabScaner.Core.Directory;

namespace LabScaner.Core.Tests.Directory;

public sealed class PersonNameTests
{
    [Fact]
    public void Parse_UpperCaseWithMiddleName_NormalizesCase()
    {
        var name = PersonName.Parse("ИВАНОВ ИВАН ИВАНОВИЧ");

        Assert.Equal(new PersonName("Иванов", "Иван", "Иванович"), name);
        Assert.Equal("Иванов И.И.", name.WithInitials);
    }

    [Fact]
    public void Parse_WithoutMiddleName()
    {
        var name = PersonName.Parse("Петрова Анна");

        Assert.Null(name.MiddleName);
        Assert.Equal("Петрова Анна", name.FullName);
        Assert.Equal("Петрова А.", name.WithInitials);
    }

    [Fact]
    public void Parse_DoubleSurname_KeepsHyphen() =>
        Assert.Equal("Петрова-Водкина", PersonName.Parse("петрова-водкина анна").LastName);

    [Fact]
    public void Parse_ExtraSpaces_Ignored() =>
        Assert.Equal("Сидоров Пётр Ильич", PersonName.Parse("  Сидоров   Пётр  Ильич ").FullName);

    [Fact]
    public void Parse_SingleWord_Throws() =>
        Assert.Throws<FormatException>(() => PersonName.Parse("Иванов"));
}
