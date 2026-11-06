using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-19 章：勢力範圍與隸屬（村莊有自己的範圍，落在城鎮範圍內就隸屬城鎮，以此類推）。</summary>
public sealed class DemoTerritoryTests
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
    public void VassalsSitInsideAHigherTierRangeAndRealmsHaveNoLiege(ulong seed)
    {
        var world = World(seed);
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin).ToList();
        Assert.True(alive.Count >= 3);
        // 指定層級：第一個是城市、第二個城鎮，其餘村莊（測規則，不靠模擬剛好長到哪一級）。
        DemoSettlementTier Tier(int id) => id == alive[0].Id ? DemoSettlementTier.City : id == alive[1].Id ? DemoSettlementTier.Town : DemoSettlementTier.Village;
        var map = DemoTerritory.Compute(world.Map, world.Sites, Tier);
        var byId = alive.ToDictionary(s => s.Id);
        int Distance(int a, int b) => DemoHexWorld.Distance(new DemoCellRef(byId[a].Column, byId[a].Row), new DemoCellRef(byId[b].Column, byId[b].Row));
        foreach (var s in alive)
        {
            // 聚落格一定屬於自己。
            Assert.Equal(s.Id, map.Cells[new DemoCellRef(s.Column, s.Row)].SettlementId);
            if (map.LiegeOf(s.Id) is { } liege)
            {
                Assert.True(Tier(liege) > Tier(s.Id));
                Assert.True(Distance(liege, s.Id) <= DemoTerritory.TempRadius(Tier(liege)));
            }
            Assert.Null(map.LiegeOf(map.RealmOf(s.Id)));
        }
        // 村莊落在城市範圍內，又沒有更近層級的城鎮涵蓋它，就直屬城市；城鎮落在城市範圍內就屬城市。
        foreach (var s in alive.Skip(2))
        {
            var inTown = Distance(alive[1].Id, s.Id) <= DemoTerritory.TempRadius(DemoSettlementTier.Town);
            var inCity = Distance(alive[0].Id, s.Id) <= DemoTerritory.TempRadius(DemoSettlementTier.City);
            Assert.Equal(inTown ? alive[1].Id : inCity ? alive[0].Id : (int?)null, map.LiegeOf(s.Id));
        }
        if (Distance(alive[0].Id, alive[1].Id) <= DemoTerritory.TempRadius(DemoSettlementTier.City))
            Assert.Equal(alive[0].Id, map.LiegeOf(alive[1].Id));
        // 每一格都在它直屬聚落的範圍內，勢力是那個聚落的最上層；水域（聚落格以外）不屬於任何勢力。
        foreach (var (cell, owner) in map.Cells)
        {
            var site = byId[owner.SettlementId];
            Assert.True(DemoHexWorld.Distance(cell, new DemoCellRef(site.Column, site.Row)) <= DemoTerritory.TempRadius(Tier(site.Id)));
            Assert.Equal(map.RealmOf(owner.SettlementId), owner.RealmId);
            Assert.True(world.Map.GetCell(cell.Column, cell.Row).Terrain != DemoTerrain.Water || cell == new DemoCellRef(site.Column, site.Row));
        }
        // 同樣的輸入同樣的結果。
        var again = DemoTerritory.Compute(world.Map, world.Sites, Tier);
        Assert.Equal(map.Cells.OrderBy(c => c.Key.Row).ThenBy(c => c.Key.Column), again.Cells.OrderBy(c => c.Key.Row).ThenBy(c => c.Key.Column));
        Assert.Equal(map.Liege.OrderBy(k => k.Key), again.Liege.OrderBy(k => k.Key));
    }

    [Fact]
    public void NamesCarryTheirTierAndDetailsDescribeTheRealm()
    {
        var world = World(1);
        var s = world.Sites.Settlements.First(x => !x.IsRuin);
        Assert.Equal(s.Name + "村", DemoSettlementHistory.TitledName(s, DemoSettlementTier.Village));
        Assert.Equal(s.Name + "鎮", DemoSettlementHistory.TitledName(s, DemoSettlementTier.Town));
        Assert.Equal(s.Name + "營", DemoSettlementHistory.TitledName(s, DemoSettlementTier.Camp));
        var map = DemoTerritory.Compute(world.Map, world.Sites, id => world.ChronicleOf(id).Tier);
        Assert.StartsWith("勢力：", DemoTerritory.Describe(map, world.Sites, s.Id, id => world.ChronicleOf(id).Tier), StringComparison.Ordinal);
        Assert.Contains(DemoPlayerDetail.Of(world, s.Column, s.Row).Tabs[0].Lines, l => l.StartsWith("勢力：", StringComparison.Ordinal));
    }
}
