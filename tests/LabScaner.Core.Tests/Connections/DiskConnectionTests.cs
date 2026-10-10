using LabScaner.Core.Connections;
using LabScaner.Core.Directory;
using LabScaner.Core.Subjects;
using LabScaner.Core.Teaching;

namespace LabScaner.Core.Tests.Connections;

public sealed class DiskConnectionTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Refresh_NeededWithinWeekOfExpiry()
    {
        var c = new DiskConnection("a", "r", _now.AddDays(30), _now);

        Assert.False(c.NeedsRefresh(_now));
        Assert.True(c.NeedsRefresh(_now.AddDays(24)));
        Assert.False(c.IsExpired(_now.AddDays(29)));
        Assert.True(c.IsExpired(_now.AddDays(30)));
    }

    [Fact]
    public void SetTokens_WithoutNewRefreshToken_KeepsOld()
    {
        var c = new DiskConnection("a", "r1", _now.AddDays(30), _now);

        c.SetTokens("a2", null, _now.AddDays(365));

        Assert.Equal("a2", c.AccessTokenProtected);
        Assert.Equal("r1", c.RefreshTokenProtected);
        Assert.Throws<ArgumentException>(() => c.SetTokens("", null, _now));
    }

    [Fact]
    public void SetAccount_StoresOwnerAndSpace()
    {
        var c = new DiskConnection("a", null, _now.AddDays(30), _now);

        c.SetAccount(new DiskInfo("v.ivanov", "Иван Иванов", 10L << 30, 1L << 30));

        Assert.Equal("v.ivanov", c.Login);
        Assert.Equal("Иван Иванов", c.DisplayName);
        Assert.Equal(10L << 30, c.TotalSpace);
    }

    [Fact]
    public void SubjectTerm_DiskSubfolders_PerGroupAndTasks()
    {
        var term = new Term(2026, TermSeason.Autumn, new DateOnly(2026, 12, 21), new DateOnly(2027, 1, 11));
        var st = new SubjectTerm(new Subject("Корпоративные информационные системы", "КорпИС"), term, 7, "КорпИС/2026-2027");
        st.AddGroup(new Group("ИСТ-42"));
        st.AddGroup(new Group("ИСТ-41"));

        Assert.Equal(["ИСТ-41 - ЛР", "ИСТ-42 - ЛР", "Задания"], st.DiskSubfolders());

        st.SetCoursework(true);
        Assert.Equal(["ИСТ-41 - ЛР", "ИСТ-41 - Кр", "ИСТ-42 - ЛР", "ИСТ-42 - Кр", "Задания"], st.DiskSubfolders());
    }
}
