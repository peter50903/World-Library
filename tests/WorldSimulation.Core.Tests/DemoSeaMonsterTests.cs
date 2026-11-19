using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-42 章：海上地下城的怪物佔海、擋住航線；只有出得了海的聚落打得了海戰。</summary>
public sealed class DemoSeaMonsterTests
{
    private static readonly Lazy<DemoObserverWorld> Large = new(() =>
        new DemoObserverWorld(WorldRecipe.ForSeeds(42UL, null, DemoMapSize.Large), DemoWeeklyTempConfig.ForagingSample));

    private static bool IsSea(DemoHexWorld map, DemoCellRef c)
        => map.Contains(c.Column, c.Row) && map.GetCell(c.Column, c.Row) is { Terrain: DemoTerrain.Water, Sea: true };

    [Fact]
    public void SeaNestsClaimTheOpenSeaWhileLandNestsStayAshore()
    {
        var map = Large.Value.Map;
        var open = map.Cells.First(c => DemoDungeons.OpenSea(map, c));
        var dungeon = new DemoCellRef(open.Column, open.Row);
        Assert.True(DemoDungeons.AtSea(map, dungeon));
        var cells = DemoMonsters.Claim(map, dungeon, 6, _ => false, atSea: true);
        Assert.Equal(dungeon, cells[0]);
        Assert.InRange(cells.Count, 7, 6 * DemoMonsters.TempSeaCellsPerMonster);
        Assert.All(cells, c =>
        {
            Assert.True(IsSea(map, c), $"({c.Column},{c.Row}) 不是海");
            Assert.InRange(DemoHexWorld.Distance(c, dungeon), 0, DemoMonsters.TempSeaMaxReach);
        });
        // 大陸上的地下城不算海上；陸地的怪物照舊不下水。
        var inland = map.Cells.First(c => c.Terrain != DemoTerrain.Water && DemoDungeons.Island(map, new DemoCellRef(c.Column, c.Row), 64).Count >= 64);
        var home = new DemoCellRef(inland.Column, inland.Row);
        Assert.False(DemoDungeons.AtSea(map, home));
        Assert.All(DemoMonsters.Claim(map, home, 6, _ => false), c => Assert.NotEqual(DemoTerrain.Water, map.GetCell(c.Column, c.Row).Terrain));
        // 每 4 個地下城最多一個在海上。
        Assert.False(DemoDungeons.SeaAllowed(2, 0));
        Assert.True(DemoDungeons.SeaAllowed(3, 0));
        Assert.False(DemoDungeons.SeaAllowed(6, 1));
        Assert.True(DemoDungeons.SeaAllowed(7, 1));
    }

    /// <summary>沿岸每隔幾格放一個假的港口，找一條航線。</summary>
    private static (IReadOnlyList<DemoSettlement> Ports, DemoSeaLane Lane) SomeLane(DemoHexWorld map)
    {
        var coast = map.Cells.Where(c => c.Terrain != DemoTerrain.Water
                && Enumerable.Range(-1, 3).Any(dr => Enumerable.Range(-1, 3).Any(dc => IsSea(map, new DemoCellRef(c.Column + dc, c.Row + dr)))))
            .OrderBy(c => c.Row).ThenBy(c => c.Column).ToList();
        var ports = new List<DemoSettlement>();
        foreach (var c in coast)
            if (ports.All(p => DemoHexWorld.Distance(new DemoCellRef(p.Column, p.Row), new DemoCellRef(c.Column, c.Row)) >= 4))
                ports.Add(new DemoSettlement(1000 + ports.Count, $"港{ports.Count}", c.Column, c.Row));
        var lanes = DemoSeaLanes.Build(map, ports, Array.Empty<DemoMonsterNest>());
        Assert.NotEmpty(lanes);
        return (ports, lanes.OrderByDescending(l => l.Length).ThenBy(l => l.From).First());
    }

    [Fact]
    public void LanesFollowTheSeaBetweenPortsAndSeaMonstersBlockThem()
    {
        var map = Large.Value.Map;
        var (ports, lane) = SomeLane(map);
        var byId = ports.ToDictionary(p => p.Id);
        var from = new DemoCellRef(byId[lane.From].Column, byId[lane.From].Row);
        var to = new DemoCellRef(byId[lane.To].Column, byId[lane.To].Row);
        Assert.True(lane.From < lane.To);
        Assert.False(lane.Blocked);
        Assert.True(DemoHexWorld.Distance(from, to) > DemoFoodTrade.TempTradeReach);
        Assert.InRange(lane.Length, 1, DemoSeaLanes.TempLaneReach);
        Assert.All(lane.Path, c => Assert.True(IsSea(map, c)));
        Assert.Equal(1, DemoHexWorld.Distance(from, lane.Path[0]));
        Assert.Equal(1, DemoHexWorld.Distance(to, lane.Path[^1]));
        for (var i = 1; i < lane.Path.Count; i++)
            Assert.Equal(1, DemoHexWorld.Distance(lane.Path[i - 1], lane.Path[i]));
        static string Key(DemoSeaLane l) => $"{l.From}-{l.To}:" + string.Join(";", l.Path.Select(c => $"{c.Column},{c.Row}"));
        Assert.Equal(DemoSeaLanes.Build(map, ports, Array.Empty<DemoMonsterNest>()).Select(Key), DemoSeaLanes.Build(map, ports, Array.Empty<DemoMonsterNest>()).Select(Key));

        // 海上怪物佔了航線中間的一格：封鎖；陸地的、沉寂的地下城不擋。
        var mid = lane.Path[lane.Path.Count / 2];
        var dungeon = new DemoDungeon(mid, "測試海淵", 2, 0, 99, lane.From);
        var nest = new DemoMonsterNest(dungeon, 6, false, 0) { CellId = 4242, AtSea = true, Cells = new[] { mid } };
        var blocked = DemoSeaLanes.Build(map, ports, new[] { nest }).Single(l => (l.From, l.To) == (lane.From, lane.To));
        Assert.True(blocked.Blocked);
        Assert.Equal((4242L, "測試海淵"), (blocked.BlockedBy, blocked.BlockedName));
        Assert.Equal(mid, blocked.Path[blocked.BlockedAt]);
        Assert.False(DemoSeaLanes.Build(map, ports, new[] { nest with { AtSea = false } }).Single(l => (l.From, l.To) == (lane.From, lane.To)).Blocked);
        Assert.False(DemoSeaLanes.Build(map, ports, new[] { nest with { Dormant = true } }).Single(l => (l.From, l.To) == (lane.From, lane.To)).Blocked);

        // 海運：暢通的航線上，港口附近賣糧的聚落賣得到對岸的市集；封鎖了就沒有。
        var at = ports.ToDictionary(p => p.Id, p => new DemoCellRef(p.Column, p.Row));
        var offers = DemoSeaLanes.Offers(new[] { lane }, at, from).ToList();
        Assert.Contains(offers, o => o.Market == lane.To && o.Distance == (lane.Length + DemoSeaLanes.TempSeaCellsPerStep - 1) / DemoSeaLanes.TempSeaCellsPerStep);
        Assert.Empty(DemoSeaLanes.Offers(new[] { blocked }, at, from));
    }

    [Fact]
    public void OnlySettlementsThatCanSailFightSeaMonstersAndOnlyCoastsAreRaided()
    {
        var world = Large.Value;
        var map = world.Map;
        var living = world.Sites.Settlements.Where(s => !s.IsRuin).ToList();
        var coastal = living.First(s => DemoMonsters.Coastal(map, s));
        var home = new DemoCellRef(coastal.Column, coastal.Row);
        // 怪物佔了這個聚落旁邊的海（3 格內所有的海）。
        var sea = map.Cells.Select(c => new DemoCellRef(c.Column, c.Row))
            .Where(c => IsSea(map, c) && DemoHexWorld.Distance(c, home) <= 3).OrderBy(c => c.Row).ThenBy(c => c.Column).ToList();
        var dungeon = new DemoDungeon(sea[0], "測試海淵", 3, 0, 99, coastal.Id);
        var nest = new DemoMonsterNest(dungeon, 12, false, 0) { CellId = DemoLandClearing.CellId(map, sea[0]), AtSea = true, Cells = sea };
        long Residents(int id) => 60;
        var none = new HashSet<int>();
        int hunts = 0, naval = 0, raids = 0;
        for (var week = 1; week <= 300; week++)
        {
            var rng = RngRegistry.Create(7);
            var landlubbers = DemoMonsters.Roll(week, DemoEra.Pioneer, map, world.Sites, Residents, new[] { nest }, none, rng);
            hunts += landlubbers.Strikes.Values.Count(s => s.Kind == DemoSettlementEventKind.MonsterHunt);
            var sailors = DemoMonsters.Roll(week, DemoEra.Pioneer, map, world.Sites, Residents, new[] { nest }, none, rng,
                sails: id => DemoMonsters.Coastal(map, living.First(s => s.Id == id)));
            foreach (var strike in sailors.Strikes.Values)
            {
                Assert.True(DemoMonsters.Coastal(map, living.First(s => s.Id == strike.SettlementId)), "海怪只打得到沿岸的聚落");
                if (strike.Kind == DemoSettlementEventKind.MonsterHunt)
                {
                    naval++;
                    Assert.Contains("海戰", strike.Cause, StringComparison.Ordinal);
                }
                else
                    raids++;
            }
        }
        Assert.Equal(0, hunts); // 沒有船就打不了海戰
        Assert.True(naval > 0, "出得了海的聚落從來沒有出海作戰");
        Assert.True(raids > 0, "海怪從來沒有襲擊沿岸");
    }
}
