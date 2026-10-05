using LabScaner.Core.Directory;

namespace LabScaner.Core.Tests.Directory;

public sealed class GroupNameTests
{
    [Theory]
    [InlineData("ИСТ-41", "ИСТ-41")]
    [InlineData("ИСТ41", "ИСТ-41")]          // имя листа в списке группы
    [InlineData("ИСТ – 41", "ИСТ-41")]       // тире с пробелами из темы письма
    [InlineData("ИСТ—41", "ИСТ-41")]
    [InlineData("ИСТ_41", "ИСТ-41")]
    [InlineData("  ИСТ-41 ", "ИСТ-41")]
    [InlineData("ИСТбд-41", "ИСТбд-41")]     // регистр сохраняется
    public void Normalize_ReturnsCanonicalName(string raw, string expected) =>
        Assert.Equal(expected, GroupName.Normalize(raw));

    [Theory]
    [InlineData("ист-41")]
    [InlineData("ИСТ41")]
    [InlineData("Ист – 41")]
    public void Key_IgnoresCaseAndDashStyle(string raw) =>
        Assert.Equal("ИСТ-41", GroupName.Key(raw));

    [Fact]
    public void DirectionOf_ReturnsLettersBeforeNumber() =>
        Assert.Equal("ИСТ", GroupName.DirectionOf("ИСТ-41"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_Empty_Throws(string raw) =>
        Assert.ThrowsAny<ArgumentException>(() => GroupName.Normalize(raw));
}
