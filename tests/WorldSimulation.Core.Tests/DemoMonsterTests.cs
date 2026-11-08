using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 12-2 章：地下城怪物的數量、勢力範圍、襲擊與討伐、沉寂與再生。</summary>
public sealed class DemoMonsterTests
{
    private static DemoSettlementEvent Ev(int week, DemoSettlementEventKind kind, long count)
        => new(week, TimeSpan.FromDays(week * 7), 1, "測試", kind, count, "測試");

    [Fact]
    public void FoldGrowsToTheCapLosesSlainMonstersAndRespawnsAfterDormancy()
    {
        var dungeon = new DemoDungeon(new DemoCellRef(3, 4), "測試洞窟", 2, 10, 99, 1);
        const long cell = 77;
        var fold = new DemoMonsters.Fold();
        DemoDungeon? At(long c) => c == cell ? dungeon : null;
        Assert.Equal(6, fold.Count(dungeon, cell, 10)); // 2 層 × 3 隻
        Assert.Equal(9, fold.Count(dungeon, cell, 10 + 3 * DemoMonsters.TempSpawnWeeks));
        Assert.Equal(16, fold.Count(dungeon, cell, 10 + 100 * DemoMonsters.TempSpawnWeeks)); // 上限 2 層 × 8 隻
        fold.Apply(Ev(34, DemoSettlementEventKind.MonsterSlain, DemoMonsters.EncodeKills(cell, 4)), At);
        Assert.Equal(5, fold.Count(dungeon, cell, 34));
        Assert.Equal(6, fold.Count(dungeon, cell, 34 + DemoMonsters.TempSpawnWeeks));
        // 別的地下城的擊殺不算。
        fold.Apply(Ev(35, DemoSettlementEventKind.MonsterSlain, DemoMonsters.EncodeKills(cell + 1, 4)), At);
        Assert.Equal(5, fold.Count(dungeon, cell, 35));
        fold.Apply(Ev(40, DemoSettlementEventKind.MonsterSlain, DemoMonsters.EncodeKills(cell, 50)), At);
        fold.Apply(Ev(40, DemoSettlementEventKind.DungeonCleared, cell), At);
        var asleep = fold.At(dungeon, cell, 200);
        Assert.True(asleep.Dormant);
        Assert.Equal((0, 0, 40), (asleep.Monsters, asleep.Radius, asleep.DormantSinceWeek));
        fold.Apply(Ev(40 + DemoMonsters.TempDormantWeeks, DemoSettlementEventKind.DungeonAwakened, cell), At);
        var awake = fold.At(dungeon, cell, 40 + DemoMonsters.TempDormantWeeks);
        Assert.False(awake.Dormant);
        Assert.Equal(6, awake.Monsters);
        Assert.Equal(DemoMonsters.Radius(6), awake.Radius);
    }

    [Fact]
    public void RaidsHitSettlementsInsideTheRangeAndHuntsKillMonstersDeterministically()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var home = world.Sites.Settlements.First(s => !s.IsRuin);
        var spot = world.Map.Cells.Where(c => c.Terrain != DemoTerrain.Water && world.Sites.SettlementAt(c.Column, c.Row) is null)
            .OrderBy(c => DemoHexWorld.Distance(new DemoCellRef(c.Column, c.Row), new DemoCellRef(home.Column, home.Row))).ThenBy(c => c.Row).ThenBy(c => c.Column)
            .First();
        var dungeon = new DemoDungeon(new DemoCellRef(spot.Column, spot.Row), "測試洞窟", 3, 0, 99, home.Id);
        var nest = new DemoMonsterNest(dungeon, 12, false, 0) { CellId = DemoLandClearing.CellId(world.Map, dungeon.Cell) };
        long Residents(int id) => 60;
        var rng = RngRegistry.Create(11);
        var none = new HashSet<int>();
        Assert.Same(DemoMonsters.Plan.Empty, DemoMonsters.Roll(5, DemoEra.Hope, world.Map, world.Sites, Residents, new[] { nest }, none, rng));
        int raids = 0, hunts = 0;
        for (var week = 1; week <= 400; week++)
        {
            var plan = DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, Residents, new[] { nest }, none, rng);
            var again = DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, Residents, new[] { nest }, none, rng);
            Assert.Equal(plan.Strikes.OrderBy(k => k.Key), again.Strikes.OrderBy(k => k.Key));
            Assert.Equal(plan.Kills, again.Kills);
            foreach (var strike in plan.Strikes.Values)
            {
                var site = world.Sites.Settlements.First(s => s.Id == strike.SettlementId);
                var distance = DemoHexWorld.Distance(new DemoCellRef(site.Column, site.Row), dungeon.Cell);
                if (strike.Kind == DemoSettlementEventKind.MonsterRaid)
                {
                    raids++;
                    Assert.True(distance <= nest.Radius + DemoMonsters.TempRaidReach);
                    Assert.Equal(1 + 12 / 5, strike.Deaths);
                }
                else
                {
                    Assert.Equal(DemoSettlementEventKind.MonsterHunt, strike.Kind);
                    hunts++;
                    Assert.True(distance <= nest.Radius + DemoMonsters.TempHuntReach);
                    Assert.InRange(strike.Deaths, 0, 12 / 4);
                }
            }
            // 擊殺不超過怪物數，殺光才沉寂；一個聚落一週只承受一件。
            Assert.InRange(plan.Kills.Sum(k => k.Kills), 0, 12);
            Assert.Equal(plan.Kills.Sum(k => k.Kills) >= 12, plan.Cleared.Count == 1);
            Assert.Equal(plan.Strikes.Count, plan.Kills.Count);
            // 已經有天災的聚落不再承受怪物。
            var taken = world.Sites.Settlements.Select(s => s.Id).ToHashSet();
            Assert.Empty(DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, Residents, new[] { nest }, taken, rng).Strikes);
        }
        Assert.InRange(raids, 40, 160); // 12 隻 × 3%＝25%（上限）
        Assert.True(hunts > 0);
        // 沉寂期滿才再生。
        var dormant = nest with { Monsters = 0, Dormant = true, DormantSinceWeek = 100 };
        Assert.Empty(DemoMonsters.Roll(100 + DemoMonsters.TempDormantWeeks - 1, DemoEra.Pioneer, world.Map, world.Sites, Residents, new[] { dormant }, none, rng).Awakened);
        var plan2 = DemoMonsters.Roll(100 + DemoMonsters.TempDormantWeeks, DemoEra.Pioneer, world.Map, world.Sites, Residents, new[] { dormant }, none, rng);
        Assert.Single(plan2.Awakened);
        var events = DemoMonsters.Events(plan2, 100 + DemoMonsters.TempDormantWeeks, TimeSpan.Zero, world.Sites, 500);
        Assert.Equal(DemoSettlementEventKind.DungeonAwakened, Assert.Single(events).Kind);
        Assert.Equal(nest.CellId, events[0].Count);
        Assert.Equal(500, events[0].EventId);
    }

    [Fact]
    public void RangeGrowsWithTheMonstersAndKillCountsRoundTrip()
    {
        Assert.Equal(0, DemoMonsters.Radius(0));
        Assert.Equal(2, DemoMonsters.Radius(1));
        Assert.Equal(3, DemoMonsters.Radius(4));
        Assert.Equal(4, DemoMonsters.Radius(24));
        Assert.Equal((1234L, 7), DemoMonsters.DecodeKills(DemoMonsters.EncodeKills(1234, 7)));
    }

    [Fact]
    public void MonstersClaimConnectedLandOutsideHumanTerritory()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var map = world.Map;
        var human = DemoTerritory.Compute(map, world.Sites, _ => DemoSettlementTier.Village).Cells;
        var dungeon = map.Cells.Where(c => c.Terrain != DemoTerrain.Water && !human.ContainsKey(new DemoCellRef(c.Column, c.Row)))
            .OrderBy(c => c.Row).ThenBy(c => c.Column).Select(c => new DemoCellRef(c.Column, c.Row)).First();
        bool Human(DemoCellRef c) => human.ContainsKey(c);
        Assert.Empty(DemoMonsters.Claim(map, dungeon, 0, Human));
        var cells = DemoMonsters.Claim(map, dungeon, 9, Human);
        Assert.Equal(dungeon, cells[0]);
        Assert.InRange(cells.Count, 1, 9 * DemoMonsters.TempCellsPerMonster);
        Assert.Equal(cells.Count, cells.Distinct().Count());
        foreach (var c in cells.Skip(1))
        {
            // 不進水、不進人類的勢力範圍、不超過最遠距離，而且和已佔的格相連。
            Assert.NotEqual(DemoTerrain.Water, map.GetCell(c.Column, c.Row).Terrain);
            Assert.False(Human(c));
            Assert.InRange(DemoHexWorld.Distance(c, dungeon), 1, DemoMonsters.TempMaxReach);
            Assert.Contains(cells, o => o != c && DemoHexWorld.Distance(o, c) == 1);
        }
        // 範圍內沒有聚落；同樣的輸入同樣的結果。
        Assert.DoesNotContain(world.Sites.Settlements, s => cells.Contains(new DemoCellRef(s.Column, s.Row)));
        Assert.Equal(cells, DemoMonsters.Claim(map, dungeon, 9, Human));
    }
}
