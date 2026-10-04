namespace LabScaner.Core.Tests;

public sealed class FakeClockTests
{
    [Fact]
    public void Advance_MovesTimeForward()
    {
        var start = new DateTimeOffset(2026, 12, 21, 9, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(start);

        clock.Advance(TimeSpan.FromDays(7));

        Assert.Equal(start.AddDays(7), clock.UtcNow);
    }
}
