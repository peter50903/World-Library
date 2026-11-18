using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-21 章：政治地圖的省份（每格陸地歸給一個聚落，長不到的是荒野；水與怪物地盤不屬於任何省份）。</summary>
public sealed class DemoProvinceMapTests
{
    private static DemoObserverWorld World(ulong seed)
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(seed, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 60; d++)
            world.AdvanceOneDay();
        return world;
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(4UL)]
    public void SettlementsOwnTheirCellsTheLandTheyUseAndNearbyLand(ulong seed)
    {
        var world = World(seed);
        var territory = world.Territory();
        DemoSettlementTier Tier(int id) => world.ChronicleOf(id).Tier;
        var nests = world.MonsterNests(world.CommittedWeeks);
        var provinces = DemoProvinceMap.Compute(world.Map, world.Sites.Settlements, territory, Tier, nests);
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin).ToDictionary(s => s.Id);
        Assert.NotEmpty(alive);
        foreach (var s in alive.Values)
            Assert.Equal(s.Id, provinces.ProvinceAt(s.Column, s.Row));
        // 實際用到的地照勢力範圍（水與怪物地盤除外）。
        foreach (var (cell, owner) in territory.Cells)
            if (DemoProvinceMap.IsLand(world.Map.GetCell(cell.Column, cell.Row)) && provinces.NestAt(cell.Column, cell.Row) == 0)
                Assert.Equal(owner.SettlementId, provinces.ProvinceAt(cell.Column, cell.Row));
        var claimed = 0;
        foreach (var cell in world.Map.Cells)
        {
            var id = provinces.ProvinceAt(cell.Column, cell.Row);
            if (!DemoProvinceMap.IsLand(cell) || provinces.NestAt(cell.Column, cell.Row) != 0)
            {
                // 海、湖、怪物地盤不屬於任何省份（聚落格例外：聚落一定是自己的）。
                if (!alive.Values.Any(s => s.Column == cell.Column && s.Row == cell.Row))
                    Assert.Equal(DemoProvinceMap.Wild, id);
                continue;
            }
            if (id == DemoProvinceMap.Wild)
                continue;
            claimed++;
            // 省份的格不是自己用到的地，就在依層級的距離內。
            var home = new DemoCellRef(alive[id].Column, alive[id].Row);
            var here = new DemoCellRef(cell.Column, cell.Row);
            Assert.True(territory.Cells.TryGetValue(here, out var used) && used.SettlementId == id
                || DemoHexWorld.Distance(here, home) * DemoProvinceMap.TempStraight <= DemoProvinceMap.TempReach(Tier(id)));
        }
        // 省份比實際用到的地大（往外長），地圖看起來才是一片一片的。
        Assert.True(claimed > territory.Cells.Count(c => DemoProvinceMap.IsLand(world.Map.GetCell(c.Key.Column, c.Key.Row))));
        // 同樣的輸入同樣的結果。
        var again = DemoProvinceMap.Compute(world.Map, world.Sites.Settlements, territory, Tier, nests);
        foreach (var cell in world.Map.Cells)
            Assert.Equal(provinces.ProvinceAt(cell.Column, cell.Row), again.ProvinceAt(cell.Column, cell.Row));
    }

    [Fact]
    public void FarLandIsWildAndMonsterLandBelongsToNoProvince()
    {
        var world = World(1);
        var site = world.Sites.Settlements.First(s => !s.IsRuin);
        var lone = new[] { new DemoSettlement(site.Id, site.Name, site.Column, site.Row) };
        // 旁邊一格陸地被怪物佔據。
        var lair = world.Map.Cells.First(c => DemoProvinceMap.IsLand(c) && !DemoProvinceMap.IsRiver(c)
            && DemoHexWorld.Distance(new DemoCellRef(c.Column, c.Row), new DemoCellRef(site.Column, site.Row)) == 1);
        var nest = new DemoMonsterNest(new DemoDungeon(new DemoCellRef(lair.Column, lair.Row), "測試洞窟", 2, 1, 99, site.Id), 6, false, 0)
        {
            Cells = new[] { new DemoCellRef(lair.Column, lair.Row) }
        };
        var provinces = DemoProvinceMap.Compute(world.Map, lone, DemoTerritoryMap.Empty, _ => DemoSettlementTier.Camp, new[] { nest });
        Assert.Equal(site.Id, provinces.ProvinceAt(site.Column, site.Row));
        Assert.Equal(DemoProvinceMap.Wild, provinces.ProvinceAt(lair.Column, lair.Row));
        Assert.Equal(1, provinces.NestAt(lair.Column, lair.Row));
        Assert.Same(nest, provinces.Nests[0]);
        // 營地最遠 2.5 格：三格以外都是荒野；地圖外也是。
        foreach (var cell in world.Map.Cells)
            if (DemoHexWorld.Distance(new DemoCellRef(cell.Column, cell.Row), new DemoCellRef(site.Column, site.Row)) >= 3)
                Assert.Equal(DemoProvinceMap.Wild, provinces.ProvinceAt(cell.Column, cell.Row));
        Assert.Equal(DemoProvinceMap.Wild, provinces.ProvinceAt(-1, 0));
        Assert.Equal(0, provinces.NestAt(world.Map.Columns, 0));
        // 遺跡不長省份。
        var ruined = new[] { lone[0] with { RuinedWeek = 5 } };
        var empty = DemoProvinceMap.Compute(world.Map, ruined, DemoTerritoryMap.Empty, _ => DemoSettlementTier.Capital);
        Assert.All(world.Map.Cells, c => Assert.Equal(DemoProvinceMap.Wild, empty.ProvinceAt(c.Column, c.Row)));
    }
}
