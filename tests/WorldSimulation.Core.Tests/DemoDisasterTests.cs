using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 12-1 章 C3a：天災的時機、地理前提、頻率與存檔。</summary>
public sealed class DemoDisasterTests
{
    [Fact]
    public void DisastersOnlyComeOnTheYearlyWeekFollowGeographyAndAreDeterministic()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var ids = world.Sites.Settlements.Select(s => s.Id).ToList();
        var rng = RngRegistry.Create(5);
        Assert.Empty(DemoDisasters.Roll(DemoDisasters.TempYearWeek + 1, world.Map, world.Sites, ids, rng));
        var all = new List<DemoDisasterStrike>();
        for (var year = 1; year <= 500; year++)
        {
            var week = year * DemoWildlife.TempYearWeeks + DemoDisasters.TempYearWeek;
            var strikes = DemoDisasters.Roll(week, world.Map, world.Sites, ids, rng);
            Assert.Equal(strikes, DemoDisasters.Roll(week, world.Map, world.Sites, ids, rng));
            all.AddRange(strikes.Values);
        }
        Assert.NotEmpty(all);
        foreach (var strike in all)
        {
            var s = world.Sites.Settlements.First(x => x.Id == strike.SettlementId);
            var origin = world.Map.GetCell(s.Column, s.Row);
            if (strike.Kind == DemoSettlementEventKind.Flood)
                Assert.Contains(world.Map.Cells, c => c.MainWaterway && !c.Sea && DemoWorldSites.HexDistance(origin, c) <= 1);
            if (strike.Kind == DemoSettlementEventKind.Earthquake)
                Assert.Contains(world.Map.Cells, c => c.Terrain == DemoTerrain.Hills && DemoWorldSites.HexDistance(origin, c) <= 2);
            Assert.InRange(strike.Deaths, 0, 4);
        }
        // 每個聚落每年最多一次，整體每年每聚落不到一成。
        Assert.InRange(all.Count, 1, 500 * ids.Count / 10);
    }

    [Fact]
    public void LivingWorldRecordsDisastersAsChronicleEventsAndSavesStayConsistent()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 60 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var lines = world.Sites.Settlements.SelectMany(s => world.ChronicleOf(s.Id).Milestones).Select(m => m.Text).ToList();
        Assert.Contains(lines, l => l.StartsWith("洪水", StringComparison.Ordinal) || l.StartsWith("地震", StringComparison.Ordinal)
            || l.StartsWith("龍捲風", StringComparison.Ordinal) || l.StartsWith("隕石", StringComparison.Ordinal));
        Assert.Empty(world.BuildCausalIndex().Problems);
        var reloaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.ExportTempSave(), reloaded.ExportTempSave());
    }
}
