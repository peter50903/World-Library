using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-10a 章 V0：世界變化報告的統計與家系源頭。</summary>
public sealed class DemoChangeReportTests
{
    private static (DemoObserverWorld World, DemoChangeTracker Tracker) Run(ulong seed, int years)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
        var tracker = new DemoChangeTracker();
        for (var year = 1; year <= years; year++)
        {
            for (var d = 0; d < TestCalendar.DaysPerYear; d++)
                world.AdvanceOneDay();
            tracker.ObserveYearEnd(world, year);
        }
        tracker.Finish(world);
        return (world, tracker);
    }

    [Fact]
    public void DecadesCountFoundingsAndRuinsOfTheWholeRun()
    {
        var (world, tracker) = Run(3, 25);

        Assert.Equal(new[] { (1, 10), (11, 20), (21, 25) }, tracker.Decades.Select(d => (d.FromYear, d.ToYear)));
        Assert.Equal(world.Sites.Settlements.Count(s => s.IsFounded), tracker.Decades.Sum(d => d.Founded));
        Assert.Equal(world.Sites.Settlements.Count(s => s.IsRuin), tracker.Decades.Sum(d => d.Ruined));

        var last = tracker.Decades[^1];
        Assert.Equal(world.WorldPopulation, last.Population);
        Assert.Equal(world.Era, last.Era);
        Assert.InRange(last.LargestLineage, last.LivingSettlements == 0 ? 0 : 1, last.LivingSettlements);
        Assert.InRange(last.Lineages, 0, last.LivingSettlements);
        Assert.InRange(tracker.Decades.Sum(d => d.FirstLearned), 0, DemoTechCatalog.All.Length);
        Assert.Equal(tracker.Decades.Sum(d => d.EraChanges) > 0, tracker.PioneerEraYear is not null);
        Assert.All(tracker.Decades, d => Assert.Equal(
            d.Founded + d.Ruined + d.TierUps + d.TierDowns + d.FirstLearned + d.Farmland + d.EraChanges, d.Major));
    }

    [Fact]
    public void LineageRootFollowsFoundersBackToAnOpeningSettlement()
    {
        var (world, _) = Run(3, 25);
        foreach (var s in world.Sites.Settlements)
        {
            var root = DemoChangeTracker.LineageRoot(world.Sites, s.Id);
            var rootSettlement = world.Sites.Settlements.Single(x => x.Id == root);
            Assert.False(rootSettlement.IsFounded);
            if (!s.IsFounded)
                Assert.Equal(s.Id, root);
            else
                Assert.Equal(DemoChangeTracker.LineageRoot(world.Sites, s.FoundedFromSettlementId), root);
        }
    }

    [Fact]
    public void SameSeedGivesTheSameReport()
    {
        var a = Run(5, 12).Tracker;
        var b = Run(5, 12).Tracker;
        Assert.Equal(a.Decades.Select(d => d.Describe()), b.Decades.Select(d => d.Describe()));
        Assert.Equal(a.Summary(), b.Summary());
    }

    [Fact]
    public void YearsMustBeContinuous()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoChangeTracker().ObserveYearEnd(world, 2));
    }
}
