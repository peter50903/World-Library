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
                    Assert.InRange(strike.Deaths, 1, 59); // 第 12-2c 節：依雙方戰力，至少留一人
                }
                else
                {
                    Assert.Equal(DemoSettlementEventKind.MonsterHunt, strike.Kind);
                    hunts++;
                    Assert.True(distance <= nest.Radius + DemoMonsters.TempHuntReach);
                    Assert.InRange(strike.Deaths, 0, 59);
                }
            }
            // 第 12-2b 節：居民只能把怪物壓到下限（每層 1 隻），殺不光、地下城不會沉寂；一個聚落一週只承受一件。
            Assert.InRange(plan.Kills.Sum(k => k.Kills), 0, 12 - DemoMonsters.SuppressFloor(dungeon));
            Assert.Empty(plan.Cleared);
            Assert.Equal(plan.Strikes.Count, plan.Kills.Count);
            // 已經有天災的聚落不再承受怪物。
            var taken = world.Sites.Settlements.Select(s => s.Id).ToHashSet();
            Assert.Empty(DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, Residents, new[] { nest }, taken, rng).Strikes);
        }
        Assert.InRange(raids, 8, 64); // 第 08-18l 章：12 隻 × 1%＝12%，上限 8%
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
    public void ResidentsOnlySuppressMonstersDownToTheFloor()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var home = world.Sites.Settlements.First(s => !s.IsRuin);
        var dungeon = new DemoDungeon(new DemoCellRef(home.Column, home.Row), "測試洞窟", 2, 0, 99, home.Id);
        var floor = DemoMonsters.SuppressFloor(dungeon);
        Assert.Equal(2 * DemoMonsters.TempSuppressFloorPerLayer, floor);
        var rng = RngRegistry.Create(5);
        var none = new HashSet<int>();
        // 已經壓到下限：大村子也不出兵討伐，被襲擊時一隻也殺不掉、地下城不沉寂。
        var low = new DemoMonsterNest(dungeon, floor, false, 0) { CellId = DemoLandClearing.CellId(world.Map, dungeon.Cell) };
        for (var week = 1; week <= 300; week++)
        {
            var plan = DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, _ => 5000, new[] { low }, none, rng);
            Assert.DoesNotContain(plan.Strikes.Values, s => s.Kind == DemoSettlementEventKind.MonsterHunt);
            Assert.All(plan.Kills, k => Assert.Equal(0, k.Kills));
            Assert.Empty(plan.Cleared);
            Assert.DoesNotContain(DemoMonsters.Events(plan, week, TimeSpan.Zero, world.Sites, 1), e => e.Kind == DemoSettlementEventKind.MonsterSlain);
        }
    }

    [Fact]
    public void OnlyFightersOfEqualOrHigherLevelMatchMonsters()
    {
        var dungeon = new DemoDungeon(new DemoCellRef(0, 0), "測試洞窟", 2, 0, 99, 1);
        var level = DemoCombat.MonsterLevel(dungeon);
        Assert.Equal(3, level);
        // 人力：村莊沒有士兵；鎮有駐軍；靠近怪物有冒險者，擊殺越多等級越高。
        bool None(string _) => false;
        Assert.Equal(new DemoCombatForce(0, 0, 0, 0, 300), DemoCombat.Force(300, DemoSettlementTier.Village, None, 0, false));
        var town = DemoCombat.Force(1000, DemoSettlementTier.Town, t => t is "TECH_ARCHERY" or "TECH_WEAPON_SMITHING", 45, true);
        Assert.Equal((30L, 3, 20L, 4, 950L), (town.Soldiers, town.SoldierLevel, town.Adventurers, town.AdventurerLevel, town.Civilians));
        Assert.Equal(4, town.BestLevel);
        Assert.True(DemoCombat.CanClear(town, level));
        // 非戰鬥人員再多人，每人戰力也遠低於怪物：擊殺占交戰怪物的比例很低、死傷多。
        var farmers = new DemoCombatForce(0, 0, 0, 0, 10_000);
        Assert.False(DemoCombat.CanClear(farmers, level));
        // 同數量、同等級的戰鬥人員勢均力敵；等級高就佔優勢。
        var equal = new DemoCombatForce(0, 0, 10, level, 0);
        var stronger = new DemoCombatForce(0, 0, 10, level + 2, 0);
        int civilianKills = 0, equalKills = 0, strongKills = 0;
        long civilianDeaths = 0, equalDeaths = 0, strongDeaths = 0;
        for (var luck = 0; luck < 100; luck++)
        {
            var c = DemoCombat.Clash(10, level, farmers, defending: true, luck);
            var e = DemoCombat.Clash(10, level, equal, defending: false, luck);
            var h = DemoCombat.Clash(10, level, stronger, defending: false, luck);
            Assert.InRange(c.Kills, 0, 10);
            civilianKills += c.Kills; equalKills += e.Kills; strongKills += h.Kills;
            civilianDeaths += c.Deaths; equalDeaths += e.Deaths; strongDeaths += h.Deaths;
        }
        Assert.True(civilianKills < equalKills && equalKills < strongKills);
        Assert.True(civilianDeaths > equalDeaths && equalDeaths > strongDeaths);
        Assert.InRange(equalKills, 250, 350); // 勢均力敵：約 10 × 0.5 × 0.6 ＝ 3 隻
        Assert.Equal((0, 0L), DemoCombat.Clash(0, level, equal, false, 50));
        Assert.Contains("只能壓制", DemoCombat.Describe(farmers));
    }

    [Fact]
    public void StrongFightersCanClearTheDungeon()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var home = world.Sites.Settlements.First(s => !s.IsRuin);
        var dungeon = new DemoDungeon(new DemoCellRef(home.Column, home.Row), "測試洞窟", 1, 0, 99, home.Id);
        var nest = new DemoMonsterNest(dungeon, 1, false, 0) { CellId = DemoLandClearing.CellId(world.Map, dungeon.Cell) };
        var heroes = new DemoCombatForce(0, 0, 20, DemoCombat.TempMaxLevel, 100);
        var rng = RngRegistry.Create(8);
        var cleared = false;
        for (var week = 1; week <= 300 && !cleared; week++)
            cleared = DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, _ => 120, new[] { nest }, new HashSet<int>(), rng, _ => heroes).Cleared.Count == 1;
        Assert.True(cleared);
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
