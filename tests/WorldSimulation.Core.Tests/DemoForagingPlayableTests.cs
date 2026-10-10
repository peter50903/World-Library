using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>Playable forest/river mode: real Core week reports, not an independent mock UI.</summary>
public sealed class DemoForagingPlayableTests
{
    [Fact]
    public void HumanForagingWorldHasNoFreeFarmNoWaterSettlementAndOnlyExplicitRiverSources()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.ForagingSample);
        Assert.True(world.UsesSharedForaging);
        Assert.Equal("森林河流生存", world.TempScenarioDisplayName);
        Assert.All(world.Sites.Settlements, s =>
            Assert.NotEqual(DemoTerrain.Water, world.Map.GetCell(s.Column, s.Row).Terrain));
        Assert.DoesNotContain(world.Sites.ResourcePoints, p => p.Kind == DemoResourceKind.Farmland);
        Assert.All(world.Sites.ResourcePoints, p => Assert.Equal(0, world.ResourceReserve(p.Id)));
        var sources = world.SharedFoodSources();
        Assert.NotEmpty(sources);
        Assert.Contains(sources, s => s.Kind == DemoCatchmentSourceKind.Forest);
        Assert.All(sources, s =>
        {
            Assert.Equal(s.Kind == DemoCatchmentSourceKind.Forest ? 20 : 5, s.FoodAvailable);
            Assert.Equal(s.Kind == DemoCatchmentSourceKind.MainRiver,
                world.IsMainRiverCell(s.Column, s.Row));
        });
    }

    [Fact]
    public void TrueWeeklySettlementUsesSharedSupplyOnceWithoutMagicFood()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.ForagingSample);
        for (int i = 0; i < 35; i++) world.AdvanceOneDay();
        Assert.Equal(5, world.WeekReports().Count);
        foreach (var report in world.WeekReports())
        {
            Assert.All(report.Settlements, l =>
            {
                Assert.True(l.IsBalanced);
                Assert.Equal(0, l.ReserveOpening);
                Assert.Equal(0, l.ReserveClosing);
                Assert.Equal(l.Harvested, l.Regenerated);
                Assert.Equal(0, l.FoodFromFarming);
                Assert.Equal(l.Harvested, l.FoodFromGathering);
            });
        }
        var latest = world.LatestWeek!;
        var harvest = world.LatestSharedHarvest()!;
        Assert.Equal(latest.Settlements.Sum(l => l.Harvested), harvest.TotalHarvested);
        var sources = world.SharedFoodSources();
        foreach (var source in sources)
            Assert.InRange(harvest.Harvests.Where(h => h.SourceId == source.Id)
                .Sum(h => h.FoodHarvested), 0, source.FoodAvailable);
        foreach (var place in world.Sites.Settlements)
            Assert.Equal(latest.Settlements.Single(l => l.SettlementId == place.Id).Harvested,
                harvest.Harvests.Where(h => h.SettlementId == place.Id).Sum(h => h.FoodHarvested));
    }

    [Fact]
    public void StoredWorldRestoresTheSameForagingWeeklyHistoryAndThenContinuesIdentically()
    {
        var first = new DemoObserverWorld(DemoScenario.Sample(7), DemoWeeklyTempConfig.ForagingSample);
        for (int i = 0; i < 91; i++) first.AdvanceOneDay();
        var save = first.ExportTempSave();
        var loaded = DemoObserverWorld.ImportTempSave(save);
        Assert.True(loaded.UsesSharedForaging);
        Assert.Equal(save, loaded.ExportTempSave());
        for (int i = 0; i < 70; i++)
        {
            first.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }
        Assert.Equal(first.ExportTempSave(), loaded.ExportTempSave());
    }

    [Fact]
    public void MultipleSeedsPreservePopulationAndFoodAccountingWithoutFarming()
    {
        foreach (var seed in new ulong[] { 1, 7, 42 })
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.ForagingSample);
            for (int day = 0; day < 365; day++) world.AdvanceOneDay();
            var reports = world.WeekReports();
            Assert.Equal(52, reports.Count);
            Assert.All(reports.SelectMany(r => r.Settlements), l =>
            {
                Assert.True(l.IsBalanced);
                Assert.Equal(0, l.FoodFromFarming);
            });
            Assert.Equal(world.SettledPopulation + world.PopulationInTransit, world.WorldPopulation);
        }
    }
}
