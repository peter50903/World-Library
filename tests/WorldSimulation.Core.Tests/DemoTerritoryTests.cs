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

    [Theory]
    [InlineData(1UL)]
    [InlineData(4UL)]
    public void RealmDetailListsTheWholeVassalTreeUnderTheTopSettlement(ulong seed)
    {
        var world = World(seed);
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin).ToList();
        DemoSettlementTier Tier(int id) => id == alive[0].Id ? DemoSettlementTier.Capital : id == alive[1].Id ? DemoSettlementTier.Town : DemoSettlementTier.Camp;
        var map = DemoTerritory.Compute(world.Map, world.Sites, Tier);
        long People(int id) => 10 + id;
        foreach (var s in alive)
        {
            // 從任何一個成員打開，都是它最上層的勢力詳情。
            var detail = DemoTerritory.Detail(map, world.Sites, s.Id, Tier, People);
            var top = map.RealmOf(s.Id);
            Assert.Equal(top, detail.SettlementId);
            var members = alive.Where(x => map.RealmOf(x.Id) == top).ToList();
            Assert.StartsWith(DemoSettlementHistory.TitledName(alive.First(x => x.Id == top), Tier(top)), detail.Title, StringComparison.Ordinal);
            Assert.Contains($"轄下聚落：{members.Count - 1} 個（直屬 {map.VassalsOf(top).Count} 個）", detail.Tabs[0].Lines);
            Assert.Contains($"勢力人口：{members.Sum(x => People(x.Id))}", detail.Tabs[0].Lines);
            // 隸屬樹：第一行是最上層，每個成員剛好一行，下級比上級多縮排一層。
            var tree = detail.Tabs[1].Lines;
            Assert.Equal(members.Count, tree.Count);
            Assert.DoesNotContain("└", tree[0], StringComparison.Ordinal);
            foreach (var m in members.Where(x => x.Id != top))
                Assert.Single(tree, line => line.TrimStart('　', '└', ' ').StartsWith(DemoSettlementHistory.TitledName(m, Tier(m.Id)) + "（", StringComparison.Ordinal));
            // 第 08-27 章：每個聚落底下多幾行（資源、兵力、人物），縮排比它自己深。
            var detailed = DemoTerritory.Detail(map, world.Sites, s.Id, Tier, People, id => new[] { $"甲{id}", $"乙{id}" }).Tabs[1].Lines;
            Assert.Equal(members.Count * 3, detailed.Count);
            foreach (var m in members)
            {
                var at = detailed.ToList().FindIndex(line => line.TrimStart('　', '└', ' ').StartsWith(DemoSettlementHistory.TitledName(m, Tier(m.Id)) + "（", StringComparison.Ordinal));
                Assert.Equal($"甲{m.Id}", detailed[at + 1].Trim('　'));
                Assert.Equal($"乙{m.Id}", detailed[at + 2].Trim('　'));
                Assert.True(detailed[at + 1].TakeWhile(ch => ch == '　').Count() > detailed[at].TakeWhile(ch => ch == '　').Count());
            }
        }
    }

    [Fact]
    public void RealmRosterListsFoodTroopsAndPeopleOfEachSettlement()
    {
        // 第 08-27 章：勢力頁每個聚落底下寫人口與存糧、兵力（戰時徵兵、常備士兵）、重要人物（領袖在前）。
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 6 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var site = world.Sites.Settlements.Where(s => !s.IsRuin).OrderByDescending(s => world.SettlementPopulation(s.Id)).First();
        var lines = DemoRealmRoster.Lines(world, site.Id);
        var ledger = world.LatestWeek!.Settlements.Single(l => l.SettlementId == site.Id);
        static string N(long v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith($"人口 {N(world.SettlementPopulation(site.Id))}｜存糧 {N(ledger.FoodClosing)}", lines[0], StringComparison.Ordinal);
        // 第 08-32 章：第二行是建材與建築。
        var holding = world.HoldingOf(site.Id)!;
        Assert.StartsWith($"建材：木 {N(holding.Wood)}、石 {N(holding.Stone)}、鐵 {N(holding.Iron)}｜住宅 {N(holding.Dwellings + holding.LargeDwellings)} 棟", lines[1], StringComparison.Ordinal);
        Assert.StartsWith($"兵力：戰時徵兵 {N(DemoRealms.Army(world.SettlementPopulation(site.Id)))} 人", lines[2], StringComparison.Ordinal);
        var leader = world.RepresentativesOf(site.Id).FirstOrDefault(r => r.Role == DemoRepresentativeRole.Leader);
        if (leader is not null)
            Assert.StartsWith($"人物：{DemoRepresentative.RoleName(DemoRepresentativeRole.Leader, world.ChronicleOf(site.Id).Tier)} {leader.Name}", lines[3], StringComparison.Ordinal);
        Assert.Empty(DemoRealmRoster.Lines(world, -1));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(4UL)]
    public void FootprintCoversBuildingsWorkplacesAndThePathsPeopleWalk(ulong seed)
    {
        var world = World(seed);
        var map = world.Map;
        foreach (var s in world.Sites.Settlements.Where(x => !x.IsRuin).Take(4))
        {
            var home = new DemoCellRef(s.Column, s.Row);
            var buildings = DemoTerritory.Footprint(map, world.Sites, s, DemoSettlementTier.Town, null);
            // 建築：聚落格在第一個，最多 5 格，都是陸地、不跨別的聚落、離聚落不超過建築距離。
            Assert.Equal(home, buildings[0]);
            Assert.InRange(buildings.Count, 1, DemoTerritory.TempBuildingCells(DemoSettlementTier.Town));
            Assert.All(buildings.Skip(1), c =>
            {
                Assert.NotEqual(DemoTerrain.Water, map.GetCell(c.Column, c.Row).Terrain);
                Assert.Null(world.Sites.SettlementAt(c.Column, c.Row));
                Assert.InRange(DemoHexWorld.Distance(c, home), 1, DemoTerritory.TempBuildingReach);
            });
            // 會狩獵時，最近的森林與走過去的路也算；都在工作距離內。
            var work = new DemoTerritoryWork(world.Land, world.Wildlife.Spells, world.CommittedWeeks, _ => true);
            var used = DemoTerritory.Footprint(map, world.Sites, s, DemoSettlementTier.Town, work);
            Assert.Equal(buildings, used.Take(buildings.Count));
            var forest = map.Within(map.GetCell(s.Column, s.Row), DemoTerritory.TempBuildingReach).Where(c => c.Terrain == DemoTerrain.Forest)
                .OrderBy(c => DemoHexWorld.Distance(c, map.GetCell(s.Column, s.Row))).ThenBy(c => c.Row).ThenBy(c => c.Column).FirstOrDefault();
            if (forest is not null)
                Assert.Contains(new DemoCellRef(forest.Column, forest.Row), used);
            Assert.All(used, c => Assert.InRange(DemoHexWorld.Distance(c, home), 0, DemoTerritory.TempWorkReach));
            Assert.Equal(used.Count, used.Distinct().Count());
        }
        // 整張地圖：每個聚落格都屬於自己。
        var map2 = world.Territory();
        foreach (var s in world.Sites.Settlements.Where(x => !x.IsRuin))
            Assert.Equal(s.Id, map2.Cells[new DemoCellRef(s.Column, s.Row)].SettlementId);
    }
}
