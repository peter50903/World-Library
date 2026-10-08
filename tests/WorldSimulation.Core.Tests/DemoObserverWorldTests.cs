using WorldSimulation.Core.World;
using Xunit;

namespace WorldSimulation.Core.Tests;

public sealed class DemoObserverWorldTests
{
    [Fact]
    public void FreshDemoStartsWithRealStateWithoutAnAlreadyCommittedEvent()
    {
        var scenario = DemoScenario.Sample(42);
        var world = new DemoObserverWorld(scenario);
        var snapshot = world.Snapshot();

        Assert.Equal(TimeSpan.Zero, snapshot.WorldTime);
        Assert.Equal(scenario.Population, snapshot.Population);
        Assert.Equal(scenario.InitialFood, snapshot.FoodStock);
        Assert.Equal(0, snapshot.DaysElapsed);
        Assert.Equal(0UL, snapshot.ResourceRngDraws);
        Assert.Empty(world.History());
    }

    [Fact]
    public void AdvanceProcessesActualScheduledDaysRatherThanUserWallClock()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42));

        var day1 = world.AdvanceOneDay();
        var day2 = world.AdvanceOneDay();

        Assert.Equal(TimeSpan.FromDays(1), day1.WorldTime);
        Assert.Equal(TimeSpan.FromDays(2), day2.WorldTime);
        Assert.Equal(1, day1.Day);
        Assert.Equal(2, day2.Day);
        Assert.Equal(TimeSpan.FromDays(2), world.Snapshot().WorldTime);
        Assert.Equal(2, world.History().Count);
    }

    [Fact]
    public void RepeatingSameSeedAndScenarioProducesIdenticalHistoryAndWorldState()
    {
        var first = new DemoObserverWorld(DemoScenario.Sample(123456));
        var second = new DemoObserverWorld(DemoScenario.Sample(123456));

        for (var i = 0; i < 35; i++)
        {
            Assert.Equal(first.AdvanceOneDay(), second.AdvanceOneDay());
        }

        Assert.Equal(first.Snapshot(), second.Snapshot());
        Assert.Equal(first.History().ToArray(), second.History().ToArray());
    }

    [Fact]
    public void EveryDayAccountsForHarvestConsumptionAndShortageExactly()
    {
        var world = new DemoObserverWorld(new DemoScenario(
            Seed: 98, Population: 6, InitialFood: 2, MaxDailyHarvest: 7, FoodPerPersonPerDay: 2));

        for (var i = 0; i < 45; i++)
        {
            var evt = world.AdvanceOneDay();
            Assert.Equal(evt.FoodBefore + evt.Harvested - evt.Consumed, evt.FoodAfter);
            Assert.Equal(12, evt.Consumed + evt.Shortage);
            Assert.InRange(evt.Harvested, 0, 7);
            Assert.True(evt.FoodAfter >= 0);
            Assert.True(evt.Shortage >= 0);
        }

        Assert.Equal(world.History().Sum(e => e.Shortage), world.Snapshot().TotalShortage);
    }

    [Fact]
    public void ReadingStatusAndHistoryDoesNotAdvanceTheWorldOrConsumeRng()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(888));
        var before = world.Snapshot();

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(before, world.Snapshot());
            Assert.Empty(world.History());
        }

        Assert.Equal(0UL, world.Snapshot().ResourceRngDraws);
    }

    [Fact]
    public void EachCommittedDemoDayConsumesExactlyOneResourceStreamDraw()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(888));

        for (var i = 1; i <= 25; i++)
        {
            world.AdvanceOneDay();
            Assert.Equal((ulong)i, world.Snapshot().ResourceRngDraws);
            Assert.Equal(i, world.Snapshot().DaysElapsed);
        }
    }

    [Fact]
    public void HistoryViewIsReadOnlyAndDetachedFromLaterCommittedEvents()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(8));
        world.AdvanceOneDay();
        var before = world.History();
        var list = Assert.IsAssignableFrom<IList<DemoDayEvent>>(before);
        Assert.Throws<NotSupportedException>(() => list.Clear());

        world.AdvanceOneDay();

        Assert.Single(before);
        Assert.Equal(2, world.History().Count);
        Assert.Equal(1, before[0].Day);
    }

    [Fact]
    public void ZeroDemoDailyDemandStillProducesAHistoryEvent()
    {
        var world = new DemoObserverWorld(new DemoScenario(
            42, Population: 1, InitialFood: 0, MaxDailyHarvest: 0, FoodPerPersonPerDay: 0));

        var evt = world.AdvanceOneDay();
        Assert.Equal(0, evt.Harvested);
        Assert.Equal(0, evt.Consumed);
        Assert.Equal(0, evt.Shortage);
        Assert.Equal(0, evt.FoodAfter);
        Assert.Single(world.History());
    }

    [Fact]
    public void PopulationRemainsExplicitlyAStaticDemoInput_NotAnInventedBirthDeathModel()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42));
        for (var i = 0; i < 30; i++) world.AdvanceOneDay();

        Assert.Equal(5, world.Snapshot().Population);
        Assert.Equal(30, world.History().Count);
    }

    [Fact]
    public void InvalidScenarioIsRejectedBeforeAnyWorldStateExists()
    {
        Assert.Throws<ArgumentNullException>(() => new DemoObserverWorld(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DemoObserverWorld(new DemoScenario(1, 0, 8, 8, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DemoObserverWorld(new DemoScenario(1, 5, -1, 8, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DemoObserverWorld(new DemoScenario(1, 5, 8, -1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DemoObserverWorld(new DemoScenario(1, 5, 8, 8, -1)));
    }
}
