using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 12-1 章 C4：地下城出現的時機、位置、命名與存檔。</summary>
public sealed class DemoDungeonTests
{
    private static int Week(int year) => year * DemoWildlife.TempYearWeeks + DemoDungeons.TempYearWeek;

    [Fact]
    public void RollFollowsEraWeekTargetAndPlacementRules()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var first = world.Sites.Settlements.OrderBy(s => s.Id).First();
        var living = new[] { first.Id }; // 只算一個有人聚落，確保地圖上有離得夠遠的丘陵或森林
        var rng = RngRegistry.Create(7);
        var none = Array.Empty<DemoDungeon>();
        IReadOnlyList<DemoSettlementEvent> Roll(int week, DemoEra era, IReadOnlyList<DemoDungeon> existing)
            => DemoDungeons.Roll(week, TimeSpan.FromDays(week * 7), era, world.Map, world.Sites, living, existing, rng, 1000);

        Assert.Empty(Roll(Week(5) + 1, DemoEra.Pioneer, none)); // 不是檢查週
        var appeared = 0;
        for (var year = 1; year <= 400; year++)
        {
            Assert.Empty(Roll(Week(year), DemoEra.Hope, none)); // 希望時代不出現
            var events = Roll(Week(year), DemoEra.Pioneer, none);
            Assert.Equal(events, Roll(Week(year), DemoEra.Pioneer, none)); // 重跑相同
            if (events.Count == 0)
                continue;
            var e = Assert.Single(events);
            appeared++;
            Assert.Equal(DemoSettlementEventKind.DungeonAppeared, e.Kind);
            Assert.Equal(first.Id, e.SettlementId);
            var cell = world.Map.GetCell(DemoLandClearing.CellOf(world.Map, e.Count).Column, DemoLandClearing.CellOf(world.Map, e.Count).Row);
            Assert.Contains(cell.Terrain, new[] { DemoTerrain.Hills, DemoTerrain.Forest });
            Assert.True(DemoWorldSites.HexDistance(world.Map.GetCell(first.Column, first.Row), cell) >= DemoDungeons.TempMinDistance);
            Assert.Null(world.Sites.SettlementAt(cell.Column, cell.Row));
        }
        Assert.InRange(appeared, 50, 150); // 25%

        // 已達目標數就不再出現。
        var full = Enumerable.Range(0, DemoDungeons.TargetCount(world.Map))
            .Select(i => new DemoDungeon(new DemoCellRef(i, 0), $"甲{i}", 1, 1, 1, first.Id)).ToList();
        Assert.All(Enumerable.Range(1, 100), y => Assert.Empty(Roll(Week(y), DemoEra.Pioneer, full)));
    }

    [Fact]
    public void SmallMapTargetsOneDungeon()
    {
        Assert.Equal(1, DemoDungeons.TargetCount(new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample).Map));
    }

    [Fact]
    public void CollectRebuildsNamesAndLayersFromEventsAndKeepsDistanceFromOtherDungeons()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var first = world.Sites.Settlements.OrderBy(s => s.Id).First();
        var rng = RngRegistry.Create(11);
        var known = new List<DemoDungeon>();
        var all = new List<DemoSettlementEvent>();
        for (var year = 1; year <= 200 && known.Count < 3; year++)
        {
            // 小地圖目標只有 1：每次都傳空清單，只用來產生事件並測重算。
            var events = DemoDungeons.Roll(Week(year), TimeSpan.Zero, DemoEra.Pioneer, world.Map, world.Sites, new[] { first.Id },
                Array.Empty<DemoDungeon>(), rng, 100 + year);
            if (events.Count == 0)
                continue;
            all.AddRange(events);
            DemoDungeons.Collect(events, world.Map, rng, known);
        }
        Assert.NotEmpty(known);
        var again = new List<DemoDungeon>();
        DemoDungeons.Collect(all, world.Map, rng, again);
        Assert.Equal(known, again); // 重算結果相同
        Assert.Equal(known.Count, known.Select(d => d.Name).Distinct().Count()); // 不重名
        foreach (var d in known)
        {
            Assert.InRange(d.Layers, DemoDungeons.TempMinLayers, DemoDungeons.TempMaxLayers);
            Assert.True(DemoNames.Length(d.Name) <= DemoNameTempSettings.Current.MaxLength);
            Assert.Contains(DemoDungeons.TempSuffixes, x => d.Name.EndsWith(x, StringComparison.Ordinal));
        }
        // 有地下城的格附近不能再出現另一個。
        var cell = world.Map.GetCell(known[0].Cell.Column, known[0].Cell.Row);
        var living = new[] { first };
        Assert.False(DemoDungeons.Eligible(world.Map, world.Sites, living, new[] { known[0].Cell }, cell));
    }

    [Fact]
    public void LivingWorldDungeonsStayWithinTargetPassCausalCheckAndSurviveSave()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 70 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        Assert.InRange(world.Dungeons.Count, 0, DemoDungeons.TargetCount(world.Map));
        Assert.Equal(world.Dungeons.Count, world.SettlementEvents().Count(e => e.Kind == DemoSettlementEventKind.DungeonAppeared));
        Assert.Empty(world.BuildCausalIndex().Problems);
        var reloaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.ExportTempSave(), reloaded.ExportTempSave());
        Assert.Equal(world.Dungeons, reloaded.Dungeons);
        foreach (var d in world.Dungeons)
            Assert.Contains(DemoPlayerDetail.Of(world, d.Cell.Column, d.Cell.Row).Tabs[0].Lines, l => l.StartsWith("地下城：" + d.Name, StringComparison.Ordinal));
    }
}
