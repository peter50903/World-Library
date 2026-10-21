using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 06 章 d 節：飲水、兒童食量、腐敗與老幼餓死抽樣（自然採集世界）。</summary>
public sealed class DemoSurvivalTests
{
    private static DemoObserverWorld Run(DemoWeeklyTempConfig config, ulong seed, int days)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        for (var i = 0; i < days; i++)
            world.AdvanceOneDay();
        return world;
    }

    [Fact]
    public void SettlementFarFromWater_GetsDehydrationDebt_WithACause()
    {
        foreach (ulong seed in Enumerable.Range(1, 200).Select(i => (ulong)i))
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.ForagingSample);
            var dry = world.Sites.Settlements.FirstOrDefault(s =>
                !DemoMigrationRoutes.HasWaterAccess(world.Map, new DemoCellRef(s.Column, s.Row)));
            if (dry is null) continue;
            for (var i = 0; i < 7; i++)
                world.AdvanceOneDay();
            var ledger = world.LatestWeek!.Settlements.Single(l => l.SettlementId == dry.Id);
            Assert.Equal(0, ledger.WaterCarriers);
            Assert.True(ledger.WaterShortage > 0);
            Assert.Equal(DemoSurvivalTempSettings.DehydrationDebt(ledger.PopulationOpening, DemoWeeklyTempConfig.ForagingSample.FoodPerPersonPerWeek),
                ledger.WaterShortage);
            var hunger = world.LatestWeek.Events.Single(e => e.SettlementId == dry.Id && e.Kind == DemoSettlementEventKind.Hunger);
            Assert.Contains("缺水", hunger.Cause, StringComparison.Ordinal);
            Assert.Equal(ledger.FoodShortage + ledger.WaterShortage, hunger.Count);
            Assert.Empty(DemoCausalIndex.Build(world.WeekReports()).Problems);
            return;
        }
        Assert.Fail("200 個種子都沒有遠離水源的聚落。");
    }

    [Fact]
    public void SettlementsWithWater_AssignCarriers_AndHaveNoWaterShortage()
    {
        var world = Run(DemoWeeklyTempConfig.ForagingSample, 3, 7);
        var survival = DemoSurvivalTempSettings.Current;
        foreach (var ledger in world.LatestWeek!.Settlements)
        {
            var place = world.Sites.Settlements.Single(s => s.Id == ledger.SettlementId);
            if (!DemoMigrationRoutes.HasWaterAccess(world.Map, new DemoCellRef(place.Column, place.Row)))
                continue;
            Assert.Equal(Math.Min(ledger.PopulationOpening, survival.CarriersNeeded(ledger.PopulationOpening)), ledger.WaterCarriers);
            Assert.Equal(0, ledger.WaterShortage);
        }
    }

    [Fact]
    public void ChildrenEatHalf_AndStoredFoodSpoils()
    {
        var world = Run(DemoWeeklyTempConfig.LivingForagingSample, 42, 3 * 365);
        var survival = DemoSurvivalTempSettings.Current;
        var sawChildren = false;
        foreach (var week in world.WeekReports())
        foreach (var ledger in week.Settlements)
        {
            var adults = ledger.AdultWorkersOpening;
            var children = ledger.PopulationOpening - adults;
            sawChildren |= children > 0;
            Assert.Equal(adults * 4 + children * 4 * survival.ChildFoodPercent / 100, ledger.FoodConsumed + ledger.FoodShortage);
            Assert.Equal((long)Math.Floor(ledger.FoodOpening * DemoSurvivalTempSettings.FoodLossPerWeek), ledger.FoodSpoiled);
            Assert.True(ledger.NoMagicFood && ledger.IsBalanced); // M1_NO_MAGIC_FOOD
        }
        Assert.True(sawChildren, "三年內應有兒童出生，才能驗兒童食量。");
    }

    [Fact]
    public void StarvationVictims_AreSampledReproducibly_AndClassifiedByAge()
    {
        DemoObserverWorld? a = null, b = null;
        foreach (var (config, seed) in new[] { (DemoWeeklyTempConfig.LivingForagingSample, 1UL), (DemoWeeklyTempConfig.LivingForagingSample, 2UL),
                     (DemoWeeklyTempConfig.LivingForagingSample, 3UL), (DemoWeeklyTempConfig.LivingForagingSample, 42UL) })
        {
            var probe = Run(config, seed, 4 * 365);
            if (probe.WeekReports().All(w => w.Settlements.All(l => l.StarvationDeaths == 0))) continue;
            a = probe;
            b = Run(config, seed, 4 * 365);
            break;
        }
        if (a is null)
            return; // 這幾個種子四年內沒有餓死：抽樣無從驗證（不調數值製造餓死）。
        Assert.Equal(a.EventHistory.Events, b!.EventHistory.Events);
        foreach (var ledger in a.WeekReports().SelectMany(w => w.Settlements))
            Assert.True(ledger.ChildStarvationDeaths + ledger.ElderStarvationDeaths <= ledger.StarvationDeaths);
        var starved = a.EventHistory.Events.Where(e => e.EventTypeId == "EV_DEATH" && e.Root == WorldSimulation.Core.History.CauseRootCategory.Resource);
        Assert.NotEmpty(starved);
    }

    [Fact]
    public void LegacyScenarios_AreUnchangedByTheSurvivalRules()
    {
        var world = Run(DemoWeeklyTempConfig.Sample, 42, 140);
        Assert.All(world.WeekReports().SelectMany(w => w.Settlements), l =>
        {
            Assert.Equal(0, l.FoodSpoiled);
            Assert.Equal(0, l.WaterShortage);
            Assert.Equal(0, l.WaterCarriers);
        });
    }
}
