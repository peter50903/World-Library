using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-8 章 B2 收尾：遷徙之民（路糧省吃、走得遠）、守土（不因擁擠分家）。</summary>
public sealed class DemoMobilityTraitTests
{
    [Fact]
    public void TraitEffectsReadTheOfficialValues()
    {
        Assert.Equal(80, DemoCultures.TravelFoodPercent(new[] { DemoCultures.WandererTrait }));
        Assert.Equal(100, DemoCultures.TravelFoodPercent(Array.Empty<string>()));
        Assert.True(DemoCultures.StaysPut(new[] { DemoCultures.RootedTrait }));
        Assert.False(DemoCultures.StaysPut(new[] { DemoCultures.WandererTrait }));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    public void LivingWorldsApplyTheTraitsAndStayConsistent(ulong seed)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
        var foodPerPerson = DemoWeeklyTempConfig.LivingForagingSample.FoodPerPersonPerWeek;
        for (var d = 0; d < 40 * TestCalendar.DaysPerYear; d++)
        {
            world.AdvanceOneDay();
            if (d % 7 != 6 || world.LatestWeek is not { } report)
                continue;
            var week = report.Week;
            // 守土：不會「在挨餓之前分出一戶」。
            foreach (var e in report.Events.Where(e => e.Kind == DemoSettlementEventKind.Emigration
                         && e.Cause.StartsWith("人口接近附近來源上限", StringComparison.Ordinal)))
                Assert.False(DemoCultures.StaysPut(world.Culture.TraitsAt(e.SettlementId, week)), $"守土聚落 #{e.SettlementId} 第 {week} 週因擁擠分家");
            // 遷徙之民：糧食夠時，途中每週只吃八成（無條件進位）。
            foreach (var m in report.Migrations.Where(m => !m.Departed && m.FoodShortage == 0))
            {
                var percent = DemoCultures.TravelFoodPercent(world.Culture.TraitsAt(m.FromSettlementId, week));
                Assert.Equal((m.PopulationOpening * foodPerPerson * percent + 99) / 100, m.FoodConsumed);
            }
        }
        Assert.Empty(world.BuildCausalIndex().Problems);
        var reloaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.ExportTempSave(), reloaded.ExportTempSave());
    }
}
