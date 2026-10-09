using LabScaner.Core.Directory;

namespace LabScaner.Core.Tests.Directory;

public sealed class TermTests
{
    [Fact]
    public void Constructor_BuildsAcademicYearAndTitle()
    {
        var term = new Term(2026, TermSeason.Autumn, new DateOnly(2026, 12, 21), new DateOnly(2027, 1, 9));

        Assert.Equal("2026-2027", term.AcademicYear);
        Assert.Equal("2026-2027, осенний", term.Title);
    }

    [Fact]
    public void SessionBeforeCreditWeek_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            new Term(2026, TermSeason.Spring, new DateOnly(2027, 6, 1), new DateOnly(2027, 5, 25)));

    [Fact]
    public void YearOutOfRange_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Term(26, TermSeason.Autumn, new DateOnly(2026, 12, 21), new DateOnly(2027, 1, 9)));
}

public sealed class TermStateTests
{
    private static readonly Term _autumn = new(2026, TermSeason.Autumn, new DateOnly(2026, 12, 21), new DateOnly(2027, 1, 11));

    [Theory]
    [InlineData(2026, 8, 31, TermState.Future)]
    [InlineData(2026, 9, 1, TermState.Current)]
    [InlineData(2027, 1, 31, TermState.Current)]
    [InlineData(2027, 2, 1, TermState.Past)]
    public void StateOn_UsesSeptemberToJanuary(int y, int m, int d, TermState expected) =>
        Assert.Equal(expected, _autumn.StateOn(new DateOnly(y, m, d)));

    [Fact]
    public void Spring_IsFebruaryToAugustOfNextYear()
    {
        var spring = new Term(2026, TermSeason.Spring, new DateOnly(2027, 5, 25), new DateOnly(2027, 6, 8));

        Assert.Equal(new DateOnly(2027, 2, 1), spring.PeriodStart);
        Assert.Equal(new DateOnly(2027, 8, 31), spring.PeriodEnd);
    }

    [Fact]
    public void DatesOutsideTerm_Throw() =>
        Assert.Throws<ArgumentException>(() =>
            new Term(2026, TermSeason.Autumn, new DateOnly(2027, 5, 25), new DateOnly(2027, 6, 8)));
}
