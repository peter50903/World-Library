using WorldSimulation.Core.Time;
using Xunit;

namespace WorldSimulation.Core.Tests;

public class WorldClockTests
{
    [Fact]
    public void Now_StartsAtZero()
    {
        Assert.Equal(TimeSpan.Zero, new WorldClock().Now);
    }

    [Fact]
    public void AdvanceTo_Forward_UpdatesNow()
    {
        var clock = new WorldClock();
        clock.AdvanceTo(TimeSpan.FromHours(3));
        Assert.Equal(TimeSpan.FromHours(3), clock.Now);
    }

    [Fact]
    public void AdvanceTo_SameTime_KeepsNow()
    {
        var clock = new WorldClock();
        clock.AdvanceTo(TimeSpan.FromSeconds(10));
        clock.AdvanceTo(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Now);
    }

    [Fact]
    public void AdvanceTo_Backwards_ThrowsAndKeepsNow()
    {
        var clock = new WorldClock();
        clock.AdvanceTo(TimeSpan.FromSeconds(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.AdvanceTo(TimeSpan.FromSeconds(9)));
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Now);
    }

    [Fact]
    public void AdvanceTo_Negative_ThrowsAndKeepsNow()
    {
        var clock = new WorldClock();
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.AdvanceTo(TimeSpan.FromSeconds(-1)));
        Assert.Equal(TimeSpan.Zero, clock.Now);
    }

    [Fact]
    public void AdvanceTo_SupportsHalfSecondPrecision()
    {
        var clock = new WorldClock();
        clock.AdvanceTo(TimeSpan.FromSeconds(0.5));
        Assert.Equal(TimeSpan.FromMilliseconds(500), clock.Now);
        clock.AdvanceTo(TimeSpan.FromSeconds(1.0));
        Assert.Equal(TimeSpan.FromSeconds(1), clock.Now);
    }
}
