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
        // 第 12-2e 節：生成週數改了，擊殺那一週的數量用生成週數算（出現時 6 隻，之後每 TempSpawnWeeks 週多一隻）。
        var killWeek = 10 + 3 * DemoMonsters.TempSpawnWeeks + 1;
        fold.Apply(Ev(killWeek, DemoSettlementEventKind.MonsterSlain, DemoMonsters.EncodeKills(cell, 4)), At);
        Assert.Equal(5, fold.Count(dungeon, cell, killWeek)); // 9 − 4
        Assert.Equal(6, fold.Count(dungeon, cell, killWeek + DemoMonsters.TempSpawnWeeks));
        // 別的地下城的擊殺不算。
        fold.Apply(Ev(killWeek + 1, DemoSettlementEventKind.MonsterSlain, DemoMonsters.EncodeKills(cell + 1, 4)), At);
        Assert.Equal(5, fold.Count(dungeon, cell, killWeek + 1));
        var clearWeek = killWeek + 6;
        fold.Apply(Ev(clearWeek, DemoSettlementEventKind.MonsterSlain, DemoMonsters.EncodeKills(cell, 50)), At);
        fold.Apply(Ev(clearWeek, DemoSettlementEventKind.DungeonCleared, cell), At);
        var asleep = fold.At(dungeon, cell, clearWeek + 160);
        Assert.True(asleep.Dormant);
        Assert.Equal((0, 0, clearWeek), (asleep.Monsters, asleep.Radius, asleep.DormantSinceWeek));
        fold.Apply(Ev(clearWeek + DemoMonsters.TempDormantWeeks, DemoSettlementEventKind.DungeonAwakened, cell), At);
        var awake = fold.At(dungeon, cell, clearWeek + DemoMonsters.TempDormantWeeks);
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
                    Assert.InRange(strike.Deaths, 1, 60 * DemoMonsters.TempRaidDeathPercent / 100); // 第 12-2g 節：一次襲擊最多死一成、至少一人
                }
                else
                {
                    Assert.Equal(DemoSettlementEventKind.MonsterHunt, strike.Kind);
                    hunts++;
                    Assert.True(distance <= nest.Radius + DemoMonsters.TempHuntReach);
                    Assert.InRange(strike.Deaths, 0, 60 * DemoMonsters.TempMilitiaDeathPercent / 100); // 第 12-2g 節：民兵最多陣亡 5%
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

    // 第 12-2j 章：黑暗時代每一週算兩週的繁衍；沉寂的地下城只要一年就甦醒。
    [Fact]
    public void DarkAgesBreedMonstersTwiceAsFastAndWakeLairsSooner()
    {
        var dungeon = new DemoDungeon(new DemoCellRef(3, 3), "測試洞窟", 3, 0, 1, 0);
        var plain = new DemoMonsters.Fold();
        var dark = new DemoMonsters.Fold { SurgeWeeks = (a, b) => b - a }; // 每一週都是黑暗時代
        var weeks = 4 * DemoMonsters.TempSpawnWeeks;
        Assert.Equal(DemoMonsters.Initial(dungeon) + 4, plain.Count(dungeon, 1, weeks));
        Assert.Equal(DemoMonsters.Initial(dungeon) + 8, dark.Count(dungeon, 1, weeks));
        Assert.Equal(DemoMonsters.Cap(dungeon), dark.Count(dungeon, 1, 100 * DemoMonsters.TempSpawnWeeks)); // 照樣有上限
        Assert.Equal(DemoMonsters.TempDarkDormantWeeks, DemoMonsters.DormantWeeks(DemoAge.Dark));
        Assert.Equal(DemoMonsters.TempDormantWeeks, DemoMonsters.DormantWeeks(DemoAge.Chaos));
        Assert.Equal(DemoMonsters.TempDormantWeeks, DemoMonsters.DormantWeeks(null));
    }

    [Fact]
    public void FullLairsSwarmAndCanWipeOutAVillageButNotATown()
    {
        // 第 12-2h 章：滿員的地下城全軍出擊；攻破村莊全村覆滅，城鎮（2 級地下城攻不破）守得住、最多死 25%；
        // 世上只剩三個以下有人的聚落時人類死守（這裡用 lastStand: 0 讓小世界也攻得破）。
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var home = world.Sites.Settlements.First(s => !s.IsRuin);
        var spot = world.Map.Cells.Where(c => c.Terrain != DemoTerrain.Water && world.Sites.SettlementAt(c.Column, c.Row) is null)
            .OrderBy(c => DemoHexWorld.Distance(new DemoCellRef(c.Column, c.Row), new DemoCellRef(home.Column, home.Row))).ThenBy(c => c.Row).ThenBy(c => c.Column)
            .First();
        var dungeon = new DemoDungeon(new DemoCellRef(spot.Column, spot.Row), "測試深淵", 2, 0, 99, home.Id);
        var full = new DemoMonsterNest(dungeon, DemoMonsters.Cap(dungeon), false, 0) { CellId = DemoLandClearing.CellId(world.Map, dungeon.Cell) };
        DemoDisasterStrike? Swarm(DemoMonsterNest nest, DemoSettlementTier tier, int weeks, out DemoMonsters.Plan? plan, int lastStand = 0)
        {
            plan = null;
            var current = nest;
            for (var week = 1; week <= weeks; week++)
            {
                var p = DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, _ => 10, new[] { current }, new HashSet<int>(), RngRegistry.Create(11),
                    tierOf: _ => tier, age: DemoAge.Dark, lastStand: lastStand);
                if (p.Strikes.Values.FirstOrDefault(s => s.Kind == DemoSettlementEventKind.MonsterSwarm) is { } hit)
                {
                    plan = p;
                    return hit;
                }
                current = MonsterTrips.After(current, p, week); // 第 08-50 章：獸潮先出巢、到了才打
            }
            return null;
        }
        var wiped = Assert.IsType<DemoDisasterStrike>(Swarm(full, DemoSettlementTier.Village, 400, out var wipedPlan));
        Assert.True(wiped.Annihilate);
        Assert.Equal(10, wiped.Deaths);
        Assert.Contains("攻破了", wiped.Cause, StringComparison.Ordinal);
        Assert.Contains(wipedPlan!.Kills, k => k.SettlementId == wiped.SettlementId && k.Kills > 0); // 出擊的怪物有耗損
        Assert.Empty(wipedPlan.Cleared);
        var held = Assert.IsType<DemoDisasterStrike>(Swarm(full, DemoSettlementTier.Town, 400, out _));
        Assert.False(held.Annihilate);
        Assert.InRange(held.Deaths, 1, 10 * DemoMonsters.TempSwarmDeathPercent / 100);
        Assert.Contains("死守", held.Cause, StringComparison.Ordinal);
        // 最後的據點：有人的聚落不多於 lastStand 個時，村莊也攻不破。
        var lastStand = Assert.IsType<DemoDisasterStrike>(Swarm(full, DemoSettlementTier.Village, 400, out _, world.Sites.Settlements.Count));
        Assert.False(lastStand.Annihilate);
        Assert.InRange(lastStand.Deaths, 1, 10 * DemoMonsters.TempSwarmDeathPercent / 100);
        // 沒有滿員不會出擊；黑暗、混沌時代機率加倍。
        Assert.Null(Swarm(full with { Monsters = DemoMonsters.Cap(dungeon) - 1 }, DemoSettlementTier.Village, 200, out _));
        // 第 12-2j 章：一層的淺洞窟滿了也不爆獸潮。
        var shallow = dungeon with { Layers = 1 };
        Assert.Null(Swarm(full with { Dungeon = shallow, Monsters = DemoMonsters.Cap(shallow) }, DemoSettlementTier.Village, 400, out _));
        Assert.Equal(2 * DemoMonsters.TempSwarmPercent, DemoMonsters.SwarmPercent(DemoAge.Chaos));
        Assert.Equal(DemoMonsters.TempSwarmPercent, DemoMonsters.SwarmPercent(DemoAge.Hope));
        Assert.Equal(DemoSettlementTier.Town, DemoMonsters.SwarmBreaks(dungeon with { Layers = 3 }));
    }

    // 第 12-2e 節：勢力的士兵組成遠征軍，能討伐離得較遠的地下城（民兵只到三格）；等級夠就殺得光。
    [Fact]
    public void SoldierExpeditionsReachFartherAndCanClearTheDungeon()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        var sites = world.Sites.Settlements.Where(s => !s.IsRuin).ToList();
        // 找一格地下城：離每個聚落都至少 6 格（範圍 2 格，離範圍至少 4 格：民兵到不了、也不會被襲擊），最近的聚落不超過 9 格（遠征軍到得了）。
        var spot = world.Map.Cells.Select(c => new DemoCellRef(c.Column, c.Row))
            .Select(c => (Cell: c, Near: sites.Select(s => (Site: s, D: DemoHexWorld.Distance(c, new DemoCellRef(s.Column, s.Row)))).OrderBy(x => x.D).ThenBy(x => x.Site.Id).First()))
            .Where(x => x.Near.D is >= 6 and <= 9)
            .Select(x => ((DemoCellRef Cell, DemoSettlement Home)?)(x.Cell, x.Near.Site)).FirstOrDefault();
        Assert.NotNull(spot);
        var (cell, home) = spot!.Value;
        var dungeon = new DemoDungeon(cell, "測試洞窟", 2, 0, 99, home.Id);
        var floor = DemoMonsters.SuppressFloor(dungeon);
        var nest = new DemoMonsterNest(dungeon, floor, false, 0) { CellId = DemoLandClearing.CellId(world.Map, dungeon.Cell) };
        var none = new HashSet<int>();
        DemoCombatForce Villagers(int _) => new(0, 0, 100);
        DemoCombatForce Army(int id) => id == home.Id ? new DemoCombatForce(10, 5, 100) : Villagers(id);
        // 只有民兵：怪物已經壓到下限，而且太遠，誰都不出兵。
        var rng = RngRegistry.Create(3);
        for (var week = 1; week <= 200; week++)
            Assert.Empty(DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, _ => 100, new[] { nest }, none, rng, Villagers).Strikes);
        // 有遠征軍：只有那個聚落出兵，等級（5）高過怪物（2），殺得光。
        var hunts = 0;
        var cleared = false;
        var current = nest;
        for (var week = 1; week <= 400 && !cleared; week++)
        {
            var plan = DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, _ => 100, new[] { current }, none, rng, Villagers, Army);
            foreach (var strike in plan.Strikes.Values.Where(s => s.Kind == DemoSettlementEventKind.MonsterHunt))
            {
                Assert.Equal(home.Id, strike.SettlementId);
                hunts++;
            }
            cleared = plan.Cleared.Count == 1;
            current = MonsterTrips.After(current with { Monsters = Math.Max(0, current.Monsters - plan.Kills.Sum(k => k.Kills)) }, plan, week); // 擊殺照事件扣掉；第 08-50 章：遠征
        }
        Assert.True(hunts > 0);
        Assert.True(cleared);
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
        Assert.Equal(2, level); // 第 12-2f 節：怪物等級＝層數
        // 人力：村莊沒有士兵；鎮有駐軍，裝備技術越多等級越高（第 12-2d 節：沒有冒險者）。
        bool None(string _) => false;
        Assert.Equal(new DemoCombatForce(0, 0, 300), DemoCombat.Force(300, DemoSettlementTier.Village, None));
        var town = DemoCombat.Force(1000, DemoSettlementTier.Town, t => t is "TECH_ARCHERY" or "TECH_WEAPON_SMITHING");
        Assert.Equal((30L, 3, 970L), (town.Soldiers, town.SoldierLevel, town.Civilians));
        Assert.Equal(3, town.BestLevel);
        Assert.True(DemoCombat.CanClear(town, level));
        // 非戰鬥人員再多人，每人戰力也遠低於怪物：擊殺占交戰怪物的比例很低、死傷多。
        var farmers = new DemoCombatForce(0, 0, 10_000);
        Assert.False(DemoCombat.CanClear(farmers, level));
        // 同數量、同等級的戰鬥人員勢均力敵；等級高就佔優勢。
        var equal = new DemoCombatForce(10, level, 0);
        var stronger = new DemoCombatForce(10, level + 2, 0);
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
        var heroes = new DemoCombatForce(20, 6, 100);
        var rng = RngRegistry.Create(8);
        var cleared = false;
        for (var week = 1; week <= 300 && !cleared; week++)
        {
            var plan = DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, _ => 120, new[] { nest }, new HashSet<int>(), rng, _ => heroes);
            cleared = plan.Cleared.Count == 1;
            nest = MonsterTrips.After(nest, plan, week); // 第 08-50 章：遠征軍先出發、到了才攻
        }
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
