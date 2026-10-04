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
