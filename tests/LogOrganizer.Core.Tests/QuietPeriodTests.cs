namespace LogOrganizer.Core.Tests;

[TestClass]
public sealed class QuietPeriodTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-17T12:00:00Z");

    [TestMethod]
    public void IsDue_RequiresFullDelaySinceLatestChange()
    {
        var quietPeriod = new QuietPeriod(TimeSpan.FromSeconds(10));

        quietPeriod.RecordChange(Start);
        quietPeriod.RecordChange(Start.AddSeconds(6));

        Assert.IsFalse(quietPeriod.IsDue(Start.AddSeconds(15)));
        Assert.IsTrue(quietPeriod.IsDue(Start.AddSeconds(16)));
    }

    [TestMethod]
    public void IsDue_IsFalseBeforeTheDelayElapses()
    {
        var quietPeriod = new QuietPeriod(TimeSpan.FromSeconds(10));
        quietPeriod.RecordChange(Start);

        Assert.IsFalse(quietPeriod.IsDue(Start));
        Assert.IsFalse(quietPeriod.IsDue(Start.AddSeconds(9)));
        Assert.IsTrue(quietPeriod.IsDue(Start.AddSeconds(10)));
    }

    [TestMethod]
    public void Reset_CancelsTheWindow()
    {
        var quietPeriod = new QuietPeriod(TimeSpan.FromSeconds(10));
        quietPeriod.RecordChange(Start);
        quietPeriod.Reset();

        Assert.IsFalse(quietPeriod.IsDue(Start.AddMinutes(5)));
    }

    [TestMethod]
    public void Constructor_RejectsNonPositiveDelay()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new QuietPeriod(TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new QuietPeriod(TimeSpan.FromSeconds(-1)));
    }
}